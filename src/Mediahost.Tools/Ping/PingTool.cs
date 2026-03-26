// CREDENTIAL SAFETY: Never log SshCredentials, WinRmCredentials, SqlCredentials, or any password/key values.
// Log only: hostname, operation name, duration, success/failure.

using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Mediahost.Tools.Interfaces;
using Mediahost.Tools.Models;
using Microsoft.Extensions.Logging;

namespace Mediahost.Tools.Ping;

public sealed class PingTool(ILogger<PingTool> logger) : IPingTool
{
    public async Task<ToolResult<PingResult>> PingAsync(
        string host,
        int timeoutMs = 3000,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var ping = new System.Net.NetworkInformation.Ping();
            var reply = await ping.SendPingAsync(host, timeoutMs);
            sw.Stop();

            var isReachable = reply.Status == IPStatus.Success;
            var rtt = reply.Status == IPStatus.Success ? reply.RoundtripTime : sw.ElapsedMilliseconds;

            logger.LogDebug("Ping {Host}: {Status} in {Ms}ms", host, reply.Status, sw.ElapsedMilliseconds);
            return ToolResult<PingResult>.Ok(
                new PingResult(isReachable, host, rtt,
                    isReachable ? null : reply.Status.ToString()),
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (
            ex is PlatformNotSupportedException
            or SocketException { SocketErrorCode: SocketError.AccessDenied }
            or InvalidOperationException)
        {
            // Raw ICMP requires elevated privileges on Linux without net_raw capability.
            // Return IsReachable=false rather than ToolResult.Fail — the tool itself worked.
            sw.Stop();
            logger.LogDebug("Ping {Host} requires elevated privileges, returning unreachable: {Message}",
                host, ex.Message);
            return ToolResult<PingResult>.Ok(
                new PingResult(false, host, sw.ElapsedMilliseconds,
                    "ICMP ping not available (run as root or use TcpProbeAsync)"),
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning("Ping {Host} unexpected error in {Ms}ms: {Message}",
                host, sw.ElapsedMilliseconds, ex.Message);
            return ToolResult<PingResult>.Fail(ex.Message, sw.ElapsedMilliseconds);
        }
    }

    public async Task<ToolResult<TcpProbeResult>> TcpProbeAsync(
        string host,
        int port,
        int timeoutMs = 5000,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cts.Token);
            sw.Stop();

            logger.LogDebug("TCP probe {Host}:{Port} open in {Ms}ms", host, port, sw.ElapsedMilliseconds);
            return ToolResult<TcpProbeResult>.Ok(
                new TcpProbeResult(true, host, port, sw.ElapsedMilliseconds, null),
                sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            logger.LogDebug("TCP probe {Host}:{Port} timed out after {Ms}ms", host, port, sw.ElapsedMilliseconds);
            return ToolResult<TcpProbeResult>.Ok(
                new TcpProbeResult(false, host, port, sw.ElapsedMilliseconds, $"Timed out after {timeoutMs}ms"),
                sw.ElapsedMilliseconds);
        }
        catch (SocketException ex)
        {
            sw.Stop();
            logger.LogDebug("TCP probe {Host}:{Port} closed in {Ms}ms: {Message}",
                host, port, sw.ElapsedMilliseconds, ex.Message);
            // Port closed/refused is not a tool error — Success=true, IsOpen=false
            return ToolResult<TcpProbeResult>.Ok(
                new TcpProbeResult(false, host, port, sw.ElapsedMilliseconds, ex.Message),
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning("TCP probe {Host}:{Port} unexpected error in {Ms}ms: {Message}",
                host, port, sw.ElapsedMilliseconds, ex.Message);
            return ToolResult<TcpProbeResult>.Fail(ex.Message, sw.ElapsedMilliseconds);
        }
    }
}
