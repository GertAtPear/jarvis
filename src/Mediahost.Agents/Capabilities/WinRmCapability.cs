using Mediahost.Tools.Interfaces;
using Mediahost.Tools.Models;
using Microsoft.Extensions.Logging;

namespace Mediahost.Agents.Capabilities;

public enum WinRmPermission
{
    ReadOnly,  // Non-destructive PowerShell/cmd only
    ReadWrite  // Any command
}

/// <summary>
/// Capability wrapper for WinRM. Enforces per-agent scope limits before delegating to IWinRmTool.
/// </summary>
public class WinRmCapability(IWinRmTool tool, ILogger<WinRmCapability> logger)
{
    private static readonly string[] ReadOnlyBlockedPatterns =
    [
        "Remove-Item", "rm ", "del ", "format ", "New-Item",
        "Set-Content", "Out-File", "Add-Content",
        "Stop-Service", "Start-Service", "Restart-Service",
        "Stop-Process", "Kill",
        "Set-ExecutionPolicy", "Invoke-Expression",
        "net stop", "net start", "sc stop", "sc start",
        ">", ">>", "| Out-File"
    ];

    public async Task<ToolResult<string>> RunCommandAsync(
        ConnectionTarget target,
        WinRmCredentials credentials,
        string psCommand,
        WinRmPermission permission = WinRmPermission.ReadOnly,
        int timeoutSeconds = 30,
        CancellationToken ct = default)
    {
        if (permission == WinRmPermission.ReadOnly)
        {
            var blocked = FindBlockedPattern(psCommand, ReadOnlyBlockedPatterns);
            if (blocked is not null)
            {
                logger.LogWarning("WinRM command blocked on {Host} under ReadOnly: pattern '{Pattern}'",
                    target.Hostname, blocked);
                return ToolResult<string>.Fail(
                    $"Command blocked: '{blocked}' is not permitted under ReadOnly access. " +
                    "Only observation commands are allowed for this agent.");
            }
        }

        return await tool.RunCommandAsync(target, credentials, psCommand, timeoutSeconds, ct);
    }

    public Task<ToolResult> TestConnectionAsync(
        ConnectionTarget target,
        WinRmCredentials credentials,
        CancellationToken ct = default)
        => tool.TestConnectionAsync(target, credentials, ct);

    private static string? FindBlockedPattern(string command, string[] patterns)
    {
        foreach (var pattern in patterns)
            if (command.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return pattern;
        return null;
    }
}
