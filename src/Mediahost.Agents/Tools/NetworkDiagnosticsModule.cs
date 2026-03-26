using System.Net.Sockets;
using System.Text.Json;
using Mediahost.Llm.Models;
using Microsoft.Extensions.Logging;

namespace Mediahost.Agents.Tools;

/// <summary>
/// Shared tool module: ping_host, dns_lookup, port_check, traceroute.
/// Pure .NET — no external package dependencies. Safe to include in any agent.
/// </summary>
public class NetworkDiagnosticsModule(ILogger<NetworkDiagnosticsModule> logger) : IToolModule
{
    private static readonly JsonSerializerOptions Opts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public IEnumerable<ToolDefinition> GetDefinitions() =>
    [
        new ToolDefinition(
            "ping_host",
            "Check if a host or IP address is reachable via TCP probe on common ports (22, 80, 443, 8080). Returns whether the host is reachable and which port responded.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "host":    { "type": "string", "description": "Hostname or IP address to check" },
                "timeout": { "type": "number", "description": "Timeout in seconds per port (default: 3)" }
              },
              "required": ["host"]
            }
            """)),

        new ToolDefinition(
            "dns_lookup",
            "Resolve a hostname to its IP address(es) or do a reverse lookup of an IP",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "host": { "type": "string", "description": "Hostname to resolve or IP address for reverse lookup" }
              },
              "required": ["host"]
            }
            """)),

        new ToolDefinition(
            "port_check",
            "Check whether a TCP port is open on a host",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "host":    { "type": "string", "description": "Hostname or IP address" },
                "port":    { "type": "number", "description": "TCP port number to check" },
                "timeout": { "type": "number", "description": "Timeout in seconds (default: 5)" }
              },
              "required": ["host", "port"]
            }
            """)),

        new ToolDefinition(
            "traceroute",
            "Run a traceroute to a host to show the network path and latency at each hop",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "host":     { "type": "string", "description": "Hostname or IP address" },
                "max_hops": { "type": "number", "description": "Maximum hops (default: 20)" }
              },
              "required": ["host"]
            }
            """))
    ];

    public async Task<string?> TryExecuteAsync(string toolName, JsonDocument input, CancellationToken ct = default)
    {
        try
        {
            return toolName switch
            {
                "ping_host"  => await PingHostAsync(input, ct),
                "dns_lookup" => await DnsLookupAsync(input, ct),
                "port_check" => await PortCheckAsync(input, ct),
                "traceroute" => await TracerouteAsync(input, ct),
                _            => null
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "NetworkDiagnosticsModule tool failed: {Tool}", toolName);
            return Err(ex.Message);
        }
    }

    // ── Tools ─────────────────────────────────────────────────────────────────

    private async Task<string> PingHostAsync(JsonDocument input, CancellationToken ct)
    {
        var host    = RequireString(input, "host");
        var timeout = input.RootElement.TryGetProperty("timeout", out var t) ? t.GetInt32() : 3;

        var probePorts = new[] { 22, 80, 443, 8080, 3000 };
        var reachable  = false;
        var openPort   = (int?)null;

        foreach (var port in probePorts)
        {
            try
            {
                using var tcp = new TcpClient();
                var connectTask = tcp.ConnectAsync(host, port, ct).AsTask();
                if (await Task.WhenAny(connectTask, Task.Delay(timeout * 1000, ct)) == connectTask
                    && connectTask.IsCompletedSuccessfully)
                {
                    reachable = true;
                    openPort  = port;
                    break;
                }
            }
            catch { /* port not open, try next */ }
        }

        return Ok(new
        {
            host,
            reachable,
            open_port = openPort,
            method    = "tcp-connect",
            note      = "ICMP ping unavailable in container; TCP probe used instead"
        });
    }

    private async Task<string> DnsLookupAsync(JsonDocument input, CancellationToken ct)
    {
        var host = RequireString(input, "host");
        var forward = await RunCommandAsync("getent", $"hosts {host}", ct);
        if (forward.ExitCode != 0)
        {
            var reverse = await RunCommandAsync("getent", $"ahosts {host}", ct);
            return Ok(new { host, resolved = false, output = reverse.Output });
        }
        return Ok(new { host, resolved = true, output = forward.Output.Trim() });
    }

    private async Task<string> PortCheckAsync(JsonDocument input, CancellationToken ct)
    {
        var host    = RequireString(input, "host");
        var port    = input.RootElement.GetProperty("port").GetInt32();
        var timeout = input.RootElement.TryGetProperty("timeout", out var t) ? t.GetInt32() : 5;

        try
        {
            using var tcp = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeout));
            await tcp.ConnectAsync(host, port, cts.Token);
            return Ok(new { host, port, open = true, message = $"{host}:{port} is reachable." });
        }
        catch (OperationCanceledException)
        {
            return Ok(new { host, port, open = false, message = $"{host}:{port} timed out after {timeout}s." });
        }
        catch (Exception ex)
        {
            return Ok(new { host, port, open = false, message = ex.Message });
        }
    }

    private async Task<string> TracerouteAsync(JsonDocument input, CancellationToken ct)
    {
        var host    = RequireString(input, "host");
        var maxHops = input.RootElement.TryGetProperty("max_hops", out var m) ? m.GetInt32() : 20;

        var result = await RunCommandAsync("tracepath", $"-m {maxHops} {host}", ct);
        if (result.ExitCode != 0)
            result = await RunCommandAsync("traceroute", $"-m {maxHops} {host}", ct);

        return Ok(new { host, output = result.Output });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<(int ExitCode, string Output)> RunCommandAsync(
        string cmd, string args, CancellationToken ct)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(cmd, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return (proc.ExitCode, (stdout + stderr).Trim());
    }

    private static string RequireString(JsonDocument doc, string key)
    {
        if (!doc.RootElement.TryGetProperty(key, out var prop))
            throw new ArgumentException($"Required parameter '{key}' is missing.");
        return prop.GetString() ?? throw new ArgumentException($"Parameter '{key}' must not be null.");
    }

    private static string Ok(object value)  => JsonSerializer.Serialize(value, Opts);
    private static string Err(string msg)   => JsonSerializer.Serialize(new { error = msg }, Opts);
}
