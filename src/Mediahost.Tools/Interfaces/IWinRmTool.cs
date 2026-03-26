using Mediahost.Tools.Models;

namespace Mediahost.Tools.Interfaces;

public interface IWinRmTool
{
    /// <summary>
    /// Executes a PowerShell command on the remote Windows host via WinRM.
    /// </summary>
    Task<ToolResult<string>> RunCommandAsync(
        ConnectionTarget target,
        WinRmCredentials credentials,
        string psCommand,
        int timeoutSeconds = 30,
        CancellationToken ct = default);

    Task<ToolResult> TestConnectionAsync(
        ConnectionTarget target,
        WinRmCredentials credentials,
        CancellationToken ct = default);
}
