using Mediahost.Tools.Models;

namespace Mediahost.Tools.Interfaces;

public record DockerContainerSummary(
    string Id,
    string Name,
    string Image,
    string State,
    string Ports,
    string Labels,
    string CreatedAt);

public record DockerContainerDetail(
    string Id,
    string Name,
    string Image,
    string State,
    IReadOnlyDictionary<string, string> EnvVars,
    IReadOnlyList<string> Ports,
    IReadOnlyDictionary<string, string> Labels);

public record DockerStats(
    string ContainerId,
    string ContainerName,
    double CpuPercent,
    long MemoryUsedBytes,
    long MemoryLimitBytes);

public interface IDockerTool
{
    /// <summary>
    /// Lists containers via SSH. Returns empty list (not an error) if Docker is not available on the host.
    /// </summary>
    Task<ToolResult<IReadOnlyList<DockerContainerSummary>>> ListContainersAsync(
        ConnectionTarget target,
        SshCredentials credentials,
        bool all = true,
        CancellationToken ct = default);

    /// <summary>
    /// Inspects a container. Sensitive env var values are REDACTED before returning.
    /// </summary>
    Task<ToolResult<DockerContainerDetail>> InspectContainerAsync(
        ConnectionTarget target,
        SshCredentials credentials,
        string containerIdOrName,
        CancellationToken ct = default);

    /// <summary>Returns raw log output for the container (last N lines).</summary>
    Task<ToolResult<string>> GetContainerLogsAsync(
        ConnectionTarget target,
        SshCredentials credentials,
        string containerIdOrName,
        int tailLines = 100,
        CancellationToken ct = default);

    Task<ToolResult<IReadOnlyList<DockerStats>>> GetStatsAsync(
        ConnectionTarget target,
        SshCredentials credentials,
        CancellationToken ct = default);
}
