using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Dapper;
using Mediahost.Agents.Services;
using Mediahost.Llm.Models;
using Nadia.Agent.Data.Repositories;
using Nadia.Agent.Services;
using Npgsql;

namespace Nadia.Agent.Tools;

public class NadiaToolExecutor(
    NetworkInterfaceRepository ifaceRepo,
    LatencyRepository latencyRepo,
    WifiNodeRepository wifiRepo,
    DnsCheckRepository dnsRepo,
    FailoverRepository failoverRepo,
    LatencyProbeService probeService,
    NpgsqlDataSource db,
    ILogger<NadiaToolExecutor> logger) : IAgentToolExecutor
{
    public IReadOnlyList<ToolDefinition> GetTools() => NadiaToolDefinitions.All;

    public async Task<string> ExecuteAsync(string toolName, JsonDocument input, CancellationToken ct = default)
    {
        try
        {
            return toolName switch
            {
                "get_network_overview" => await GetNetworkOverviewAsync(),
                "get_interface_status" => await GetInterfaceStatusAsync(input),
                "get_latency_history"  => await GetLatencyHistoryAsync(input),
                "get_wifi_inventory"   => await GetWifiInventoryAsync(),
                "get_failover_history" => await GetFailoverHistoryAsync(input),
                "run_ping"             => await RunPingAsync(input, ct),
                "run_traceroute"       => await RunTracerouteAsync(input, ct),
                "check_dns"            => await CheckDnsAsync(input, ct),
                "get_vpn_status"       => await GetVpnStatusAsync(ct),
                "register_interface"   => await RegisterInterfaceAsync(input),
                _ => $"{{\"error\": \"Unknown tool: {toolName}\"}}"
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Nadia] Tool {Tool} failed", toolName);
            return $"{{\"error\": \"{ex.Message.Replace("\"", "'")}\"}}";
        }
    }

    private async Task<string> GetNetworkOverviewAsync()
    {
        var interfaces = await ifaceRepo.GetAllAsync();
        var failovers  = await failoverRepo.GetRecentAsync(3);
        var result = new List<object>();
        foreach (var iface in interfaces)
        {
            var avgRtt = await latencyRepo.GetAverageRttAsync(iface.Id, 1);
            result.Add(new { iface, recentAvgRttMs = avgRtt });
        }
        return JsonSerializer.Serialize(new { interfaces = result, recentFailovers = failovers });
    }

    private async Task<string> GetInterfaceStatusAsync(JsonDocument input)
    {
        var name = GetString(input, "name");
        var iface = await ifaceRepo.GetByNameAsync(name);
        if (iface is null) return $"{{\"error\": \"Interface '{name}' not found\"}}";
        var history = await latencyRepo.GetHistoryAsync(iface.Id, 6);
        return JsonSerializer.Serialize(new { interface_ = iface, latencyHistory = history });
    }

    private async Task<string> GetLatencyHistoryAsync(JsonDocument input)
    {
        var name  = GetString(input, "interface_name");
        var hours = input.RootElement.TryGetProperty("hours", out var h) ? h.GetInt32() : 24;
        var iface = await ifaceRepo.GetByNameAsync(name);
        if (iface is null) return $"{{\"error\": \"Interface '{name}' not found\"}}";
        var history = await latencyRepo.GetHistoryAsync(iface.Id, hours);
        return JsonSerializer.Serialize(history);
    }

    private async Task<string> GetWifiInventoryAsync()
    {
        var nodes = await wifiRepo.GetAllAsync();
        return JsonSerializer.Serialize(nodes);
    }

    private async Task<string> GetFailoverHistoryAsync(JsonDocument input)
    {
        var limit = input.RootElement.TryGetProperty("limit", out var l) ? l.GetInt32() : 10;
        var events = await failoverRepo.GetRecentAsync(limit);
        return JsonSerializer.Serialize(events);
    }

    private async Task<string> RunPingAsync(JsonDocument input, CancellationToken ct)
    {
        var host = GetString(input, "host");
        var (rtt, loss) = await probeService.PingAsync(host, ct);
        return JsonSerializer.Serialize(new { host, rttMs = rtt, packetLossPct = loss });
    }

    private static async Task<string> RunTracerouteAsync(JsonDocument input, CancellationToken ct)
    {
        var host = GetString(input, "host");
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo("tracepath", host)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false
            };
            proc.Start();
            var output = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            return JsonSerializer.Serialize(new { host, output = output.Length > 2000 ? output[..2000] : output });
        }
        catch (Exception ex)
        {
            return $"{{\"error\": \"{ex.Message.Replace("\"", "'")}\", \"host\": \"{host}\"}}";
        }
    }

    private static async Task<string> CheckDnsAsync(JsonDocument input, CancellationToken ct)
    {
        var hostname = GetString(input, "hostname");
        var sw = Stopwatch.StartNew();
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(hostname, ct);
            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                hostname,
                resolvedAddresses = addresses.Select(a => a.ToString()),
                resolutionMs = sw.ElapsedMilliseconds
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            return $"{{\"error\": \"{ex.Message.Replace("\"", "'")}\", \"hostname\": \"{hostname}\"}}";
        }
    }

    private async Task<string> GetVpnStatusAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            var facts = await conn.QueryAsync<(string key, string value)>(
                "SELECT fact_key, fact_value FROM andrew_schema.network_facts WHERE fact_key LIKE 'vpn_%'");
            return JsonSerializer.Serialize(facts.ToDictionary(f => f.key, f => f.value));
        }
        catch
        {
            return "{\"status\": \"unknown\", \"reason\": \"andrew_schema not available\"}";
        }
    }

    private async Task<string> RegisterInterfaceAsync(JsonDocument input)
    {
        var name        = GetString(input, "name");
        var displayName = GetString(input, "display_name");
        var ifType      = GetString(input, "if_type");
        var ipAddress   = input.RootElement.TryGetProperty("ip_address", out var ip) ? ip.GetString() : null;
        var subnet      = input.RootElement.TryGetProperty("subnet", out var s) ? s.GetString() : null;
        await ifaceRepo.UpsertAsync(name, displayName, ifType, ipAddress, subnet);
        return $"{{\"status\": \"registered\", \"name\": \"{name}\"}}";
    }

    private static string GetString(JsonDocument doc, string key) =>
        doc.RootElement.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";
}
