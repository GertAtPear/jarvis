using Mediahost.Shared.Services;
using Mediahost.Tools.Interfaces;
using Mediahost.Tools.Models;
using Microsoft.Extensions.Logging;

namespace Mediahost.Agents.Capabilities;

public enum SshPermission
{
    ReadOnly,   // Non-destructive commands only (ps, cat, ls, docker ps, etc.)
    ReadWrite,  // Any command — restarts, writes, installs
    DeployOnly  // docker pull, docker compose up/down/restart only
}

/// <summary>
/// Capability wrapper for SSH. Enforces per-agent scope limits before delegating to ISshTool.
/// </summary>
public class SshCapability(ISshTool tool, IVaultService vault, ILogger<SshCapability> logger)
{
    private static readonly string[] ReadOnlyBlockedPatterns =
    [
        "rm ", "rm -", "rmdir", "mv ", "cp ", "chmod", "chown",
        "dd ", "mkfs", "fdisk", "parted",
        "apt ", "apt-get", "yum ", "dnf ", "pip install", "npm install",
        "systemctl start", "systemctl stop", "systemctl restart",
        "service ", "kill ", "pkill ", "killall",
        "docker rm", "docker stop", "docker start", "docker restart",
        "docker exec", "docker run", "docker pull", "docker compose up",
        "docker compose down", "docker compose restart",
        ">", ">>", "tee ",
        "sudo su", "su root"
    ];

    private static readonly string[] DeployOnlyAllowedPrefixes =
    [
        "docker pull",
        "docker compose",
        "docker stack"
    ];

    /// <summary>
    /// Fetches SSH credentials for the given hostname from vault path /servers/{hostname}.
    /// Returns null if credentials are not found.
    /// </summary>
    public async Task<SshCredentials?> GetCredentialsAsync(
        string hostname, CancellationToken ct = default)
    {
        Dictionary<string, string> secrets;
        try { secrets = await vault.GetSecretsBulkAsync($"/servers/{hostname}", ct); }
        catch { return null; }

        secrets.TryGetValue("ssh_user", out var user);
        secrets.TryGetValue("ssh_key_path", out var keyPath);
        secrets.TryGetValue("ssh_password", out var password);

        user ??= "root";

        if (!string.IsNullOrWhiteSpace(keyPath))
            return SshCredentials.FromKeyFile(user, keyPath);
        if (!string.IsNullOrWhiteSpace(password))
            return SshCredentials.FromPassword(user, password);

        return null;
    }

    public async Task<ToolResult> RunCommandAsync(
        ConnectionTarget target,
        SshCredentials credentials,
        string command,
        SshPermission permission = SshPermission.ReadOnly,
        CancellationToken ct = default)
    {
        if (permission == SshPermission.ReadOnly)
        {
            var blocked = FindBlockedPattern(command, ReadOnlyBlockedPatterns);
            if (blocked is not null)
            {
                logger.LogWarning("SSH command blocked on {Host} under ReadOnly: pattern '{Pattern}'",
                    target.Hostname, blocked);
                return ToolResult.Fail(
                    $"Command blocked: '{blocked}' is not permitted under ReadOnly access. " +
                    "Only observation commands are allowed for this agent.");
            }
        }

        if (permission == SshPermission.DeployOnly)
        {
            var trimmed = command.TrimStart();
            var allowed = DeployOnlyAllowedPrefixes.Any(p =>
                trimmed.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            if (!allowed)
            {
                logger.LogWarning("SSH command blocked on {Host} under DeployOnly: '{Command}'",
                    target.Hostname, trimmed[..Math.Min(60, trimmed.Length)]);
                return ToolResult.Fail(
                    "Command blocked: only docker pull, docker compose, and docker stack commands " +
                    "are permitted under DeployOnly access.");
            }
        }

        var result = await tool.RunCommandAsync(target, credentials, command, ct);
        return result.Success
            ? ToolResult.Ok(result.DurationMs)
            : ToolResult.Fail(result.ErrorMessage!, result.DurationMs);
    }

    /// <summary>
    /// Runs a command and returns stdout as a string, or null on failure.
    /// </summary>
    public async Task<string?> RunAndReadAsync(
        ConnectionTarget target,
        SshCredentials credentials,
        string command,
        SshPermission permission = SshPermission.ReadOnly,
        CancellationToken ct = default)
    {
        if (permission == SshPermission.ReadOnly)
        {
            var blocked = FindBlockedPattern(command, ReadOnlyBlockedPatterns);
            if (blocked is not null)
            {
                logger.LogWarning("SSH command blocked on {Host} under ReadOnly: pattern '{Pattern}'",
                    target.Hostname, blocked);
                return null;
            }
        }

        if (permission == SshPermission.DeployOnly)
        {
            var trimmed = command.TrimStart();
            var allowed = DeployOnlyAllowedPrefixes.Any(p =>
                trimmed.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            if (!allowed) return null;
        }

        var result = await tool.RunCommandAsync(target, credentials, command, ct);
        return result.Success ? result.Value : null;
    }

    private static string? FindBlockedPattern(string command, string[] patterns)
    {
        foreach (var pattern in patterns)
            if (command.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return pattern;
        return null;
    }
}
