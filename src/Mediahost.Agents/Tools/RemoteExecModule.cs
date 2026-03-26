using System.Text.Json;
using Mediahost.Agents.Services;
using Mediahost.Llm.Models;
using Mediahost.Tools.Interfaces;
using Mediahost.Tools.Models;
using Microsoft.Extensions.Logging;

namespace Mediahost.Agents.Tools;

/// <summary>
/// Shared tool module: ssh_exec, winrm_exec.
/// Depends on IServerResolver (agent-specific), ISshTool and IWinRmTool (from Mediahost.Tools).
/// </summary>
public class RemoteExecModule(
    IServerResolver serverResolver,
    ISshTool sshTool,
    IWinRmTool winRmTool,
    ILogger<RemoteExecModule> logger) : IToolModule
{
    private static readonly JsonSerializerOptions Opts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public IEnumerable<ToolDefinition> GetDefinitions() =>
    [
        new ToolDefinition(
            "ssh_exec",
            "Execute a shell command on a registered server via SSH. Use for docker operations, service management, file inspection, log viewing, etc. Returns stdout/stderr and exit code.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "server":  { "type": "string", "description": "Server hostname, name, or IP address as registered" },
                "command": { "type": "string", "description": "The shell command to run on the remote server" },
                "timeout": { "type": "number", "description": "Timeout in seconds (default: 30)" }
              },
              "required": ["server", "command"]
            }
            """)),

        new ToolDefinition(
            "winrm_exec",
            "Execute a PowerShell command on a registered Windows server via WinRM. Use for service management, " +
            "log inspection (Get-WinEvent, Get-Content), process management, file checks, etc. Returns stdout/stderr and exit code.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "server":  { "type": "string", "description": "Server hostname, name, or IP address as registered" },
                "command": { "type": "string", "description": "The PowerShell command to run on the remote Windows server" },
                "timeout": { "type": "number", "description": "Timeout in seconds (default: 30)" }
              },
              "required": ["server", "command"]
            }
            """))
    ];

    public async Task<string?> TryExecuteAsync(string toolName, JsonDocument input, CancellationToken ct = default)
    {
        try
        {
            return toolName switch
            {
                "ssh_exec"   => await SshExecAsync(input, ct),
                "winrm_exec" => await WinRmExecAsync(input, ct),
                _            => null
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RemoteExecModule tool failed: {Tool}", toolName);
            return Err(ex.Message);
        }
    }

    // ── Tools ─────────────────────────────────────────────────────────────────

    private async Task<string> SshExecAsync(JsonDocument input, CancellationToken ct)
    {
        var serverQuery = RequireString(input, "server");
        var command     = RequireString(input, "command");

        var info = await serverResolver.ResolveAsync(serverQuery, ct);
        if (info is null)
            return Err($"No registered server found matching '{serverQuery}'.");

        info.Secrets.TryGetValue("ssh_user",     out var sshUser);
        info.Secrets.TryGetValue("ssh_key_path", out var sshKeyPath);
        info.Secrets.TryGetValue("ssh_password", out var sshPassword);
        info.Secrets.TryGetValue("ssh_port",     out var sshPortStr);

        if (string.IsNullOrWhiteSpace(sshKeyPath) && string.IsNullOrWhiteSpace(sshPassword))
            return Err($"No SSH credentials in vault for '{info.Hostname}'. Add ssh_user and ssh_password (or ssh_key_path).");

        sshUser ??= "root";
        var sshPort = int.TryParse(sshPortStr, out var p) ? p : 22;

        SshCredentials credentials;
        try
        {
            credentials = !string.IsNullOrWhiteSpace(sshKeyPath)
                ? SshCredentials.FromKeyFile(sshUser, sshKeyPath)
                : SshCredentials.FromPassword(sshUser, sshPassword!);
        }
        catch (Exception ex)
        {
            return Err($"Failed to load SSH credentials: {ex.Message}");
        }

        var target = new ConnectionTarget(info.Hostname, info.Host, sshPort, OsType.Linux);
        var result = await sshTool.RunCommandAsync(target, credentials, command, ct);

        logger.LogInformation("[ssh_exec] {Host}: {Command} → {Status}", info.Host, command,
            result.Success ? "ok" : "fail");

        if (!result.Success)
            return Err($"SSH connection to {info.Host} failed: {result.ErrorMessage}");

        return Ok(new
        {
            server    = info.Hostname,
            command,
            stdout    = (result.Value ?? "").Trim(),
            stderr    = "",
            exit_code = 0,
            success   = true
        });
    }

    private async Task<string> WinRmExecAsync(JsonDocument input, CancellationToken ct)
    {
        var serverQuery = RequireString(input, "server");
        var command     = RequireString(input, "command");
        var timeout     = input.RootElement.TryGetProperty("timeout", out var t) ? t.GetInt32() : 30;

        var info = await serverResolver.ResolveAsync(serverQuery, ct);
        if (info is null)
            return Err($"No registered server found matching '{serverQuery}'.");

        info.Secrets.TryGetValue("winrm_user",     out var winrmUser);
        info.Secrets.TryGetValue("winrm_password", out var winrmPassword);
        info.Secrets.TryGetValue("winrm_port",     out var winrmPortStr);

        if (string.IsNullOrWhiteSpace(winrmUser) || string.IsNullOrWhiteSpace(winrmPassword))
            return Err($"No WinRM credentials in vault for '{info.Hostname}'. Add winrm_user and winrm_password.");

        var winrmPort = int.TryParse(winrmPortStr, out var p) ? p : 5985;
        var credentials = new WinRmCredentials(winrmUser, winrmPassword);
        var target = new ConnectionTarget(info.Hostname, info.Host, winrmPort, OsType.Windows);

        var result = await winRmTool.RunCommandAsync(target, credentials, command, timeout, ct);
        logger.LogInformation("[winrm_exec] {Host}: {Command} → {Status}", info.Host, command,
            result.Success ? "ok" : "fail");

        if (!result.Success)
            return Err($"WinRM connection to {info.Host} failed: {result.ErrorMessage}");

        return Ok(new
        {
            server    = info.Hostname,
            command,
            stdout    = result.Value ?? "",
            stderr    = "",
            exit_code = 0,
            success   = true
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string RequireString(JsonDocument doc, string key)
    {
        if (!doc.RootElement.TryGetProperty(key, out var prop))
            throw new ArgumentException($"Required parameter '{key}' is missing.");
        return prop.GetString() ?? throw new ArgumentException($"Parameter '{key}' must not be null.");
    }

    private static string Ok(object value)  => JsonSerializer.Serialize(value, Opts);
    private static string Err(string msg)   => JsonSerializer.Serialize(new { error = msg }, Opts);
}
