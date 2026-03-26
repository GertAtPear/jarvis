using Mediahost.Tools.Models;

namespace Mediahost.Tools.Interfaces;

public record PingResult(
    bool IsReachable,
    string Host,
    long RoundTripMs,
    string? ErrorMessage);

public record TcpProbeResult(
    bool IsOpen,
    string Host,
    int Port,
    long ResponseTimeMs,
    string? ErrorMessage);

public interface IPingTool
{
    /// <summary>
    /// ICMP ping. On Linux without raw socket privileges, returns IsReachable=false with an error message.
    /// Use TcpProbeAsync as a fallback.
    /// </summary>
    Task<ToolResult<PingResult>> PingAsync(
        string host,
        int timeoutMs = 3000,
        CancellationToken ct = default);

    /// <summary>
    /// TCP port probe. ToolResult.Success=false only on unexpected tool exceptions;
    /// a closed port returns Success=true, IsOpen=false.
    /// </summary>
    Task<ToolResult<TcpProbeResult>> TcpProbeAsync(
        string host,
        int port,
        int timeoutMs = 5000,
        CancellationToken ct = default);
}
