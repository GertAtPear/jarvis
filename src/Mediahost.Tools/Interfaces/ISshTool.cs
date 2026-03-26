using Mediahost.Tools.Models;

namespace Mediahost.Tools.Interfaces;

public interface ISshTool
{
    /// <summary>
    /// Opens a fresh SSH connection, executes the command, disconnects, and returns stdout.
    /// Returns <c>ToolResult.Success=false</c> on connection or authentication failure.
    /// </summary>
    Task<ToolResult<string>> RunCommandAsync(
        ConnectionTarget target,
        SshCredentials credentials,
        string command,
        CancellationToken ct = default);

    Task<ToolResult> TestConnectionAsync(
        ConnectionTarget target,
        SshCredentials credentials,
        CancellationToken ct = default);
}
