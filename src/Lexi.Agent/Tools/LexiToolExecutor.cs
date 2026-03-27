using System.Diagnostics;
using System.Text.Json;
using Dapper;
using Lexi.Agent.Data.Repositories;
using Lexi.Agent.Services;
using Mediahost.Agents.Data;
using Mediahost.Agents.Services;
using Mediahost.Llm.Models;
using Npgsql;

namespace Lexi.Agent.Tools;

public class LexiToolExecutor(
    CertificateRepository certRepo,
    OpenPortRepository portRepo,
    AnomalyRepository anomalyRepo,
    CveRepository cveRepo,
    SoftwareRepository softwareRepo,
    NetworkDeviceRepository deviceRepo,
    CertCheckService certService,
    NetworkScanService networkScanService,
    IAgentMemoryService memory,
    NpgsqlDataSource db,
    ILogger<LexiToolExecutor> logger) : IAgentToolExecutor
{
    public IReadOnlyList<ToolDefinition> GetTools() => LexiToolDefinitions.All;

    public async Task<string> ExecuteAsync(string toolName, JsonDocument input, CancellationToken ct = default)
    {
        try
        {
            return toolName switch
            {
                "get_security_overview"  => await GetSecurityOverviewAsync(ct),
                "get_certificate_status" => await GetCertStatusAsync(input),
                "get_expiring_certs"     => await GetExpiringCertsAsync(input),
                "get_open_ports"         => await GetOpenPortsAsync(input),
                "get_access_anomalies"   => await GetAccessAnomaliesAsync(input),
                "get_cve_alerts"         => await GetCveAlertsAsync(input),
                "get_software_inventory" => await GetSoftwareInventoryAsync(input),
                "get_network_devices"    => await GetNetworkDevicesAsync(ct),
                "run_cert_check"         => await RunCertCheckAsync(input, ct),
                "run_port_scan"          => await RunPortScanAsync(input, ct),
                "run_network_scan"       => await RunNetworkScanAsync(ct),
                "mark_anomaly_resolved"  => await MarkAnomalyResolvedAsync(input),
                "mark_port_expected"     => await MarkPortExpectedAsync(input),
                "mark_cve_acknowledged"  => await MarkCveAcknowledgedAsync(input),
                "mark_device_known"      => await MarkDeviceKnownAsync(input, ct),
                _ => $"{{\"error\": \"Unknown tool: {toolName}\"}}"
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Lexi] Tool {Tool} failed", toolName);
            return $"{{\"error\": \"{ex.Message.Replace("\"", "'")}\"}}";
        }
    }

    private async Task<string> GetSecurityOverviewAsync(CancellationToken ct)
    {
        var expiring        = await certRepo.GetExpiringAsync(30);
        var unexpectedPorts = await portRepo.GetAllAsync(unexpectedOnly: true);
        var anomalies       = await anomalyRepo.GetUnresolvedAsync();
        var cves            = await cveRepo.GetUnacknowledgedAsync();
        var unknown         = await deviceRepo.GetUnknownAsync();
        return JsonSerializer.Serialize(new
        {
            expiringCertCount   = expiring.Count(),
            unexpectedPortCount = unexpectedPorts.Count(),
            unresolvedAnomalies = anomalies.Count(),
            unacknowledgedCves  = cves.Count(),
            unknownDevices      = unknown.Count()
        });
    }

    private async Task<string> GetCertStatusAsync(JsonDocument input)
    {
        var host = GetStringOrNull(input, "host");
        if (host is not null)
        {
            var cert = await certRepo.GetByHostAsync(host);
            return JsonSerializer.Serialize(cert);
        }
        var all = await certRepo.GetAllAsync();
        return JsonSerializer.Serialize(all);
    }

    private async Task<string> GetExpiringCertsAsync(JsonDocument input)
    {
        var days = input.RootElement.TryGetProperty("days", out var d) ? d.GetInt32() : 30;
        var certs = await certRepo.GetExpiringAsync(days);
        return JsonSerializer.Serialize(certs);
    }

    private async Task<string> GetOpenPortsAsync(JsonDocument input)
    {
        var host           = GetStringOrNull(input, "host");
        var unexpectedOnly = input.RootElement.TryGetProperty("unexpected_only", out var u) && u.GetBoolean();
        var ports = await portRepo.GetAllAsync(host, unexpectedOnly);
        return JsonSerializer.Serialize(ports);
    }

    private async Task<string> GetAccessAnomaliesAsync(JsonDocument input)
    {
        var anomalies = await anomalyRepo.GetUnresolvedAsync();
        var sourceIp  = GetStringOrNull(input, "source_ip");
        if (sourceIp is not null)
            anomalies = anomalies.Where(a => a.SourceIp == sourceIp);
        return JsonSerializer.Serialize(anomalies);
    }

    private async Task<string> GetCveAlertsAsync(JsonDocument input)
    {
        var severity = GetStringOrNull(input, "severity");
        var cves = await cveRepo.GetUnacknowledgedAsync(severity);
        var package = GetStringOrNull(input, "package");
        if (package is not null)
            cves = cves.Where(c => c.AffectedSoftware?.Contains(package, StringComparison.OrdinalIgnoreCase) == true);
        return JsonSerializer.Serialize(cves);
    }

    private async Task<string> GetSoftwareInventoryAsync(JsonDocument input)
    {
        var host = GetStringOrNull(input, "host");
        var pm   = GetStringOrNull(input, "package_manager");
        var sw   = await softwareRepo.GetByHostAsync(host, pm);
        return JsonSerializer.Serialize(sw);
    }

    private async Task<string> GetNetworkDevicesAsync(CancellationToken ct)
    {
        var devices = (await deviceRepo.GetAllAsync()).ToList();

        // Cross-reference with Nadia's WiFi nodes (read-only)
        List<object> wifiNodes = [];
        try
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            var nodes = await conn.QueryAsync<dynamic>(
                "SELECT ssid, bssid, connected_clients, scanned_at FROM nadia_schema.wifi_nodes ORDER BY scanned_at DESC");
            wifiNodes = nodes.Cast<object>().ToList();
        }
        catch { /* nadia_schema may not be populated yet */ }

        return JsonSerializer.Serialize(new { devices, wifiNodes });
    }

    private async Task<string> RunCertCheckAsync(JsonDocument input, CancellationToken ct)
    {
        var host = GetString(input, "host");
        var port = input.RootElement.TryGetProperty("port", out var p) ? p.GetInt32() : 443;
        return await certService.CheckSingleAsync(host, port, ct);
    }

    private async Task<string> RunPortScanAsync(JsonDocument input, CancellationToken ct)
    {
        var host = GetString(input, "host");
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo("nmap", $"-sT -F {host}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false
            };
            proc.Start();
            var output = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            // Parse and store open ports
            foreach (var line in output.Split('\n'))
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    line, @"^(\d+)/(tcp|udp)\s+open\s+(\S*)");
                if (match.Success)
                {
                    var port    = int.Parse(match.Groups[1].Value);
                    var proto   = match.Groups[2].Value;
                    var service = match.Groups[3].Value;
                    await portRepo.UpsertAsync(host, port, proto, service, "open");
                }
            }

            return JsonSerializer.Serialize(new { host, output = output.Length > 3000 ? output[..3000] : output });
        }
        catch (Exception ex)
        {
            return $"{{\"error\": \"{ex.Message.Replace("\"", "'")}\", \"host\": \"{host}\"}}";
        }
    }

    private async Task<string> RunNetworkScanAsync(CancellationToken ct)
    {
        await networkScanService.ScanAsync(ct);
        var unknown = await deviceRepo.GetUnknownAsync();
        return JsonSerializer.Serialize(new
        {
            status = "scan complete",
            unknownDevices = unknown.Count(),
            devices = unknown
        });
    }

    private async Task<string> MarkAnomalyResolvedAsync(JsonDocument input)
    {
        if (!Guid.TryParse(GetString(input, "id"), out var id))
            return "{\"error\": \"Invalid ID\"}";
        var notes = GetStringOrNull(input, "notes");
        await anomalyRepo.MarkResolvedAsync(id, notes);
        return $"{{\"status\": \"resolved\", \"id\": \"{id}\"}}";
    }

    private async Task<string> MarkPortExpectedAsync(JsonDocument input)
    {
        if (!Guid.TryParse(GetString(input, "id"), out var id))
            return "{\"error\": \"Invalid ID\"}";
        await portRepo.MarkExpectedAsync(id);
        return $"{{\"status\": \"marked as expected\", \"id\": \"{id}\"}}";
    }

    private async Task<string> MarkCveAcknowledgedAsync(JsonDocument input)
    {
        if (!Guid.TryParse(GetString(input, "id"), out var id))
            return "{\"error\": \"Invalid ID\"}";
        var reason = GetStringOrNull(input, "reason");
        await cveRepo.AcknowledgeAsync(id, reason);
        return $"{{\"status\": \"acknowledged\", \"id\": \"{id}\"}}";
    }

    private async Task<string> MarkDeviceKnownAsync(JsonDocument input, CancellationToken ct)
    {
        if (!Guid.TryParse(GetString(input, "id"), out var id))
            return "{\"error\": \"Invalid ID\"}";
        var deviceName = GetString(input, "device_name");
        var notes      = GetStringOrNull(input, "notes");
        await deviceRepo.MarkKnownAsync(id, deviceName, notes);
        // Also remember it as a fact
        await memory.RememberFactAsync($"device_{id}", $"{deviceName}: {notes ?? "known device"}", ct);
        return $"{{\"status\": \"marked as known\", \"id\": \"{id}\", \"deviceName\": \"{deviceName}\"}}";
    }

    private static string GetString(JsonDocument doc, string key) =>
        doc.RootElement.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";

    private static string? GetStringOrNull(JsonDocument doc, string key) =>
        doc.RootElement.TryGetProperty(key, out var v) ? v.GetString() : null;
}
