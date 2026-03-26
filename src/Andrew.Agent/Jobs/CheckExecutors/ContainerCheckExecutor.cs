using Andrew.Agent.Data.Repositories;
using Andrew.Agent.Models;
using Mediahost.Shared.Services;
using Renci.SshNet;

namespace Andrew.Agent.Jobs.CheckExecutors;

/// <summary>
/// Live SSH check: connects to the server and runs "docker ps" to verify
/// the target container is actually running right now.
/// Falls back to the knowledge store if SSH credentials are unavailable.
/// </summary>
public class ContainerCheckExecutor(
    ServerRepository servers,
    ContainerRepository containers,
    IScopedVaultService vault,
    IConfiguration config,
    ILogger<ContainerCheckExecutor> logger)
{
    public async Task<(bool ok, string details)> ExecuteAsync(
        ScheduledCheck check, CancellationToken ct)
    {
        // ── Resolve the server ─────────────────────────────────────────────
        ServerInfo? server = null;

        if (check.ServerId.HasValue)
        {
            server = await servers.GetByIdAsync(check.ServerId.Value);
        }
        else
        {
            // Find the first server that has a container matching the target name
            var matches = await containers.SearchByNameAsync(check.Target);
            var first = matches.FirstOrDefault();
            if (first?.ServerId != null)
                server = await servers.GetByIdAsync(first.ServerId);
        }

        // ── Attempt live SSH check ─────────────────────────────────────────
        if (server is not null)
        {
            var (sshOk, sshDetails) = await LiveSshCheckAsync(server, check.Target, ct);
            if (sshOk.HasValue)
                return (sshOk.Value, sshDetails);
            // SSH unavailable — fall through to DB
            logger.LogDebug("SSH unavailable for {Host}, falling back to knowledge store", server.Hostname);
        }

        // ── DB fallback ────────────────────────────────────────────────────
        return await KnowledgeStoreCheckAsync(check.Target, check.ServerId, ct);
    }

    private async Task<(bool? ok, string details)> LiveSshCheckAsync(
        ServerInfo server, string containerTarget, CancellationToken ct)
    {
        var secretPath = server.VaultSecretPath ?? $"/servers/{server.Hostname}";

        var sshUser = await vault.GetSecretAsync(secretPath, "ssh_user", ct);
        if (string.IsNullOrEmpty(sshUser)) return (null, "No SSH credentials in vault");

        Renci.SshNet.ConnectionInfo connInfo;
        var keyPath = await vault.GetSecretAsync(secretPath, "ssh_key_path", ct);
        var sshPass = await vault.GetSecretAsync(secretPath, "ssh_password", ct);

        if (!string.IsNullOrEmpty(keyPath) && File.Exists(keyPath))
        {
            connInfo = new Renci.SshNet.ConnectionInfo(server.IpAddress, server.SshPort, sshUser,
                new PrivateKeyAuthenticationMethod(sshUser, new PrivateKeyFile(keyPath)));
        }
        else if (!string.IsNullOrEmpty(sshPass))
        {
            connInfo = new Renci.SshNet.ConnectionInfo(server.IpAddress, server.SshPort, sshUser,
                new PasswordAuthenticationMethod(sshUser, sshPass));
        }
        else
        {
            return (null, "No SSH credentials found in vault");
        }

        try
        {
            using var client = new SshClient(connInfo);
            client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(
                config.GetValue<int>("Andrew:Ssh:TimeoutSeconds", 10));

            await Task.Run(() => client.Connect(), ct);

            // docker ps --filter matches on partial name
            var cmd = client.RunCommand(
                $"docker ps --filter 'name={containerTarget}' --filter 'status=running' --format '{{{{.Names}}}}\t{{{{.Status}}}}' 2>/dev/null");
            var output = cmd.Result.Trim();

            client.Disconnect();

            if (string.IsNullOrEmpty(output))
                return (false,
                    $"No running container matching '{containerTarget}' on {server.Hostname} (live check)");

            var firstLine = output.Split('\n')[0];
            return (true,
                $"Container running on {server.Hostname}: {firstLine} (live SSH check)");
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "SSH check failed for {Host}", server.Hostname);
            return (null, $"SSH connect failed: {ex.Message}");
        }
    }

    private async Task<(bool ok, string details)> KnowledgeStoreCheckAsync(
        string target, Guid? serverId, CancellationToken ct)
    {
        IEnumerable<ContainerInfo> matches;

        if (serverId.HasValue)
        {
            var all = await containers.GetByServerIdAsync(serverId.Value);
            matches = all.Where(c =>
                c.Name?.Contains(target, StringComparison.OrdinalIgnoreCase) == true ||
                c.Image?.Contains(target, StringComparison.OrdinalIgnoreCase) == true);
        }
        else
        {
            matches = await containers.SearchByNameAsync(target);
        }

        var list = matches.ToList();
        if (list.Count == 0)
            return (false, $"No container matching '{target}' found in knowledge store");

        var running = list.FirstOrDefault(c =>
            c.Status?.Contains("running", StringComparison.OrdinalIgnoreCase) == true);

        var stale = list.Any(c => c.ScannedAt < DateTime.UtcNow.AddHours(-4));
        var staleNote = stale ? " ⚠️ knowledge store data may be stale (>4h)" : "";

        if (running is not null)
            return (true,
                $"Container '{running.Name}' is running (status: {running.Status}, scanned: {running.ScannedAt:g}){staleNote}");

        var stopped = list.First();
        return (false,
            $"Container '{stopped.Name}' is NOT running (status: {stopped.Status}, scanned: {stopped.ScannedAt:g}){staleNote}");
    }
}
