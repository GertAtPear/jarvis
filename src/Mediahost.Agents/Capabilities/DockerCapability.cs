using Mediahost.Tools.Interfaces;
using Mediahost.Tools.Models;

namespace Mediahost.Agents.Capabilities;

/// <summary>
/// Capability wrapper for Docker. All methods are read-only by design — pass-through to IDockerTool.
/// Agents call this instead of IDockerTool directly.
/// </summary>
public class DockerCapability(IDockerTool docker)
{
    public Task<ToolResult<IReadOnlyList<DockerContainerSummary>>> ListContainersAsync(
        ConnectionTarget target,
        SshCredentials credentials,
        bool all = true,
        CancellationToken ct = default)
        => docker.ListContainersAsync(target, credentials, all, ct);

    public Task<ToolResult<DockerContainerDetail>> InspectContainerAsync(
        ConnectionTarget target,
        SshCredentials credentials,
        string containerIdOrName,
        CancellationToken ct = default)
        => docker.InspectContainerAsync(target, credentials, containerIdOrName, ct);

    public Task<ToolResult<string>> GetContainerLogsAsync(
        ConnectionTarget target,
        SshCredentials credentials,
        string containerIdOrName,
        int tailLines = 100,
        CancellationToken ct = default)
        => docker.GetContainerLogsAsync(target, credentials, containerIdOrName, tailLines, ct);

    public Task<ToolResult<IReadOnlyList<DockerStats>>> GetContainerStatsAsync(
        ConnectionTarget target,
        SshCredentials credentials,
        CancellationToken ct = default)
        => docker.GetStatsAsync(target, credentials, ct);
}
