using System.Net.Sockets;
using Andrew.Agent.Data.Repositories;
using Andrew.Agent.Models;

namespace Andrew.Agent.Jobs.CheckExecutors;

/// <summary>
/// TCP-based liveness check. No SSH required — just probes whether a port is open.
/// Used for both server_up (probes SSH port) and port_listening (probes any port).
/// </summary>
public class ServerUpCheckExecutor(ServerRepository servers)
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    // ── server_up ─────────────────────────────────────────────────────────────

    public async Task<(bool ok, string details)> ExecuteAsync(
        ScheduledCheck check, CancellationToken ct)
    {
        // Target is a hostname — look up its IP from the DB for accuracy
        var server = await servers.GetByHostnameAsync(check.Target);
        var host = server?.IpAddress ?? check.Target;
        var port = server?.SshPort ?? 22;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var reachable = await TcpConnectAsync(host, port, DefaultTimeout);
        sw.Stop();

        if (reachable)
        {
            // Sync status to DB if changed
            if (server is not null && server.Status != "online")
                await servers.UpdateStatusAsync(server.Id, "online");

            return (true, $"{check.Target} is reachable on port {port} ({sw.ElapsedMilliseconds}ms)");
        }
        else
        {
            if (server is not null && server.Status == "online")
                await servers.UpdateStatusAsync(server.Id, "offline");

            return (false, $"{check.Target} is NOT reachable on port {port} (TCP connect timed out after {(int)DefaultTimeout.TotalSeconds}s)");
        }
    }

    // ── port_listening ────────────────────────────────────────────────────────

    public async Task<(bool ok, string details)> ExecutePortAsync(
        ScheduledCheck check, CancellationToken ct)
    {
        // Target format: "hostname:port" or "ip:port"
        if (!TryParseHostPort(check.Target, out var host, out var port))
            return (false, $"Invalid target format '{check.Target}'. Use hostname:port or ip:port.");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var open = await TcpConnectAsync(host, port, DefaultTimeout);
        sw.Stop();

        return open
            ? (true,  $"Port {port} on {host} is OPEN ({sw.ElapsedMilliseconds}ms)")
            : (false, $"Port {port} on {host} is CLOSED / unreachable");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<bool> TcpConnectAsync(string host, int port, TimeSpan timeout)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var timeoutTask = Task.Delay(timeout);
            var winner = await Task.WhenAny(connectTask, timeoutTask);
            return winner == connectTask && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseHostPort(string target, out string host, out int port)
    {
        host = "";
        port = 0;

        var lastColon = target.LastIndexOf(':');
        if (lastColon <= 0) return false;

        host = target[..lastColon];
        return int.TryParse(target[(lastColon + 1)..], out port) && port is > 0 and <= 65535;
    }
}
