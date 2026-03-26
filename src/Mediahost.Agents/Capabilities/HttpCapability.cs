using Mediahost.Tools.Interfaces;
using Mediahost.Tools.Models;

namespace Mediahost.Agents.Capabilities;

/// <summary>
/// Capability wrapper for HTTP checks and ping. All operations are read-only by nature.
/// Pass-through with consistent interface for agents.
/// </summary>
public class HttpCapability(IHttpCheckTool httpCheck, IPingTool ping)
{
    public Task<ToolResult<HttpCheckResult>> CheckAsync(
        string url,
        int timeoutSeconds = 10,
        bool followRedirects = true,
        CancellationToken ct = default)
        => httpCheck.CheckAsync(url, timeoutSeconds, followRedirects, ct);

    public Task<ToolResult<PingResult>> PingAsync(
        string host,
        int timeoutMs = 3000,
        CancellationToken ct = default)
        => ping.PingAsync(host, timeoutMs, ct);

    public Task<ToolResult<TcpProbeResult>> TcpProbeAsync(
        string host,
        int port,
        int timeoutMs = 5000,
        CancellationToken ct = default)
        => ping.TcpProbeAsync(host, port, timeoutMs, ct);
}
