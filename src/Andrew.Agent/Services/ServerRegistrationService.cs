using System.Net.Sockets;
using Andrew.Agent.Data.Repositories;
using Andrew.Agent.Models;
using Mediahost.Agents.Services;
using Mediahost.Shared.Services;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Andrew.Agent.Services;

public class ServerRegistrationService(
    ServerRepository servers,
    IScopedVaultService vault,
    ISshDiscoveryService discovery,
    IWindowsDiscoveryService windowsDiscovery,
    IConfiguration config,
    ILogger<ServerRegistrationService> logger)
{
    public async Task<ServerRegistrationSession> StartRegistrationAsync(
        string hostname, string ipAddress, int sshPort, string? notes,
        string connectionType = "ssh")
    {
        var vaultPath = $"/servers/{hostname}";

        var server = new ServerInfo
        {
            Id = Guid.NewGuid(),
            Hostname = hostname,
            IpAddress = ipAddress,
            SshPort = sshPort,
            Status = "pending_credentials",
            VaultSecretPath = vaultPath,
            Notes = notes,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        ServerTagHelper.ApplyConnectionType(server, connectionType);

        await servers.UpsertAsync(server);

        return new ServerRegistrationSession
        {
            VaultSecretPath = vaultPath,
            Instructions = BuildInstructions(hostname, connectionType)
        };
    }

    public async Task<ActivationResult> ActivateServerAsync(string hostname)
    {
        var server = await servers.GetByHostnameAsync(hostname);
        if (server is null)
            return new ActivationResult(false, "Server not registered", null);

        var connectionType = ServerTagHelper.GetConnectionType(server);
        var vaultPath = $"/servers/{hostname}";
        Dictionary<string, string> secrets;
        try
        {
            secrets = await vault.GetSecretsBulkAsync(vaultPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read vault at {Path}", vaultPath);
            secrets = [];
        }

        if (connectionType == "winrm")
            return await ActivateWinRmServerAsync(server, secrets, vaultPath);

        return await ActivateSshServerAsync(server, secrets, vaultPath);
    }

    private async Task<ActivationResult> ActivateSshServerAsync(
        ServerInfo server, Dictionary<string, string> secrets, string vaultPath)
    {
        var hostname = server.Hostname;
        secrets.TryGetValue("ssh_user",     out var sshUser);
        secrets.TryGetValue("ssh_password", out var sshPassword);
        secrets.TryGetValue("ssh_key_path", out var sshKeyPath);

        if (string.IsNullOrWhiteSpace(sshKeyPath) && string.IsNullOrWhiteSpace(sshPassword))
        {
            return new ActivationResult(false,
                $"No credentials found at vault path {vaultPath}.\n\n{BuildInstructions(hostname)}",
                null);
        }

        sshUser ??= "root";
        var sshPort = server.SshPort > 0 ? server.SshPort : 22;

        AuthenticationMethod authMethod;
        try
        {
            authMethod = !string.IsNullOrWhiteSpace(sshKeyPath)
                ? new PrivateKeyAuthenticationMethod(sshUser, new PrivateKeyFile(sshKeyPath))
                : new PasswordAuthenticationMethod(sshUser, sshPassword!);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load SSH credentials for {Host}", hostname);
            await servers.UpdateStatusAsync(server.Id, "credential_error");
            return new ActivationResult(false,
                $"SSH credential error: {ex.Message}. Check credentials at vault path {vaultPath}", null);
        }

        var connInfo = new Renci.SshNet.ConnectionInfo(server.Hostname, sshPort, sshUser, authMethod)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        try
        {
            using var ssh = new SshClient(connInfo);
            ssh.Connect();
            using var cmd = ssh.RunCommand("echo CONNECTION_OK");
        }
        catch (Exception ex) when (ex is SshException or SocketException or InvalidOperationException)
        {
            logger.LogWarning(ex, "SSH connection failed to {Host}:{Port}", hostname, sshPort);
            await servers.UpdateStatusAsync(server.Id, "credential_error");
            return new ActivationResult(false,
                $"SSH connection failed: {ex.Message}. Check credentials at vault path {vaultPath}", null);
        }

        await servers.UpdateStatusAsync(server.Id, "online");
        var updated = await servers.GetByHostnameAsync(hostname) ?? server;
        var result = await discovery.DiscoverServerAsync(updated);
        return new ActivationResult(true, null, result);
    }

    private async Task<ActivationResult> ActivateWinRmServerAsync(
        ServerInfo server, Dictionary<string, string> secrets, string vaultPath)
    {
        var hostname = server.Hostname;
        secrets.TryGetValue("winrm_user",     out var winrmUser);
        secrets.TryGetValue("winrm_password", out var winrmPassword);
        secrets.TryGetValue("winrm_port",     out var winrmPortStr);

        if (string.IsNullOrWhiteSpace(winrmUser) || string.IsNullOrWhiteSpace(winrmPassword))
        {
            return new ActivationResult(false,
                $"No WinRM credentials found at vault path {vaultPath}.\n\n{BuildInstructions(hostname, "winrm")}",
                null);
        }

        var winrmPort = int.TryParse(winrmPortStr, out var p) ? p : 5985;
        var host = string.IsNullOrWhiteSpace(server.IpAddress) ? server.Hostname : server.IpAddress;

        // Test connection via a simple Get-Date command
        try
        {
            var winrm = new WinRmService(logger as ILogger<WinRmService>
                ?? new Logger<WinRmService>(new LoggerFactory()));
            var test = await winrm.ExecuteAsync(host, winrmPort, winrmUser, winrmPassword,
                "Get-Date -Format 'yyyy-MM-dd HH:mm:ss'");
            if (test.ExitCode != 0 && string.IsNullOrWhiteSpace(test.Stdout))
                throw new InvalidOperationException(test.Stderr);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "WinRM connection test failed to {Host}:{Port}", hostname, winrmPort);
            await servers.UpdateStatusAsync(server.Id, "credential_error");
            return new ActivationResult(false,
                $"WinRM connection failed: {ex.Message}. Ensure WinRM is enabled (run 'winrm quickconfig -q' on {hostname}) and check credentials at {vaultPath}.",
                null);
        }

        await servers.UpdateStatusAsync(server.Id, "online");
        var updated = await servers.GetByHostnameAsync(hostname) ?? server;
        var result = await windowsDiscovery.DiscoverServerAsync(updated);
        return new ActivationResult(true, null, result);
    }

    private string BuildInstructions(string hostname, string connectionType = "ssh")
    {
        var vaultBaseUrl = config["Infisical:BaseUrl"] ?? "https://app.infisical.com";

        if (connectionType == "winrm")
        {
            return $"""
                ## Add WinRM credentials for {hostname}

                1. Open the Infisical vault: **{vaultBaseUrl}**
                2. Go to your project → Secrets → environment: **prod**
                3. Navigate to path: **/servers/{hostname}**
                4. Add these secrets:

                | Key | Value |
                |-----|-------|
                | `winrm_user` | Windows username (e.g. `Administrator` or `DOMAIN\user`) |
                | `winrm_password` | Windows password |
                | `winrm_port` | Optional — defaults to `5985` |

                > **Prerequisite**: Run `winrm quickconfig -q` in elevated PowerShell on {hostname} once.

                Once done, say: **"Andrew, activate {hostname}"**
                """;
        }

        return $"""
            ## Add SSH credentials for {hostname}

            1. Open the Infisical vault: **{vaultBaseUrl}**
            2. Go to your project → Secrets → environment: **prod**
            3. Navigate to path: **/servers/{hostname}**
            4. Add these secrets:

            | Key | Value |
            |-----|-------|
            | `ssh_user` | The SSH username (e.g. `ubuntu`, `deploy`, `root`) |
            | `ssh_password` | The SSH password **OR** leave blank if using a key |
            | `ssh_key_path` | `/app/ssh-keys/yourkey.pem` if using key-based auth |

            > **Key-based auth**: Place the .pem file in the `./ssh-keys/` folder on the
            > AI platform server. It is mounted read-only into Andrew's container.

            Once done, say: **"Andrew, activate {hostname}"**
            """;
    }
}
