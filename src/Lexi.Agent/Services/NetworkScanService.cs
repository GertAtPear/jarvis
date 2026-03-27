using System.Diagnostics;
using System.Text.RegularExpressions;
using Dapper;
using Lexi.Agent.Data.Repositories;
using Npgsql;

namespace Lexi.Agent.Services;

public class NetworkScanService(
    NetworkDeviceRepository deviceRepo,
    ScanLogRepository scanLogRepo,
    NpgsqlDataSource db,
    ILogger<NetworkScanService> logger)
{
    public async Task ScanAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var found = new List<(string Mac, string? Ip, string? Hostname, string? Vendor)>();

        try
        {
            // Get subnets from Nadia's network_interfaces (cross-schema, read-only)
            List<string> subnets;
            try
            {
                await using var conn = await db.OpenConnectionAsync(ct);
                subnets = (await conn.QueryAsync<string>(
                    "SELECT subnet FROM nadia_schema.network_interfaces WHERE subnet IS NOT NULL AND is_active = true")).ToList();
            }
            catch
            {
                subnets = [];
            }

            if (subnets.Count == 0)
            {
                // Fallback: scan ARP cache
                found.AddRange(await ScanArpCacheAsync(ct));
            }
            else
            {
                foreach (var subnet in subnets)
                {
                    found.AddRange(await ScanSubnetAsync(subnet, ct));
                }
            }

            if (found.Count > 0)
                await deviceRepo.BulkUpsertAsync(found);

            sw.Stop();

            // Log unknown devices found
            var unknown = await deviceRepo.GetUnknownAsync();
            var unknownCount = unknown.Count();
            if (unknownCount > 0)
                logger.LogWarning("[Lexi] {Count} unknown network devices detected", unknownCount);

            await scanLogRepo.InsertAsync("network_scan", "success", subnets.Count, found.Count, (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "[Lexi] NetworkScanService failed");
            await scanLogRepo.InsertAsync("network_scan", "error", 0, 0, (int)sw.ElapsedMilliseconds);
        }
    }

    private static async Task<List<(string Mac, string? Ip, string? Hostname, string? Vendor)>> ScanArpCacheAsync(CancellationToken ct)
    {
        var result = new List<(string Mac, string? Ip, string? Hostname, string? Vendor)>();
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo("arp", "-n")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            proc.Start();
            var output = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            foreach (var line in output.Split('\n').Skip(1))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && parts[2].Contains(':'))
                    result.Add((parts[2], parts[0], null, null));
            }
        }
        catch { /* arp may not be available */ }
        return result;
    }

    private static async Task<List<(string Mac, string? Ip, string? Hostname, string? Vendor)>> ScanSubnetAsync(string subnet, CancellationToken ct)
    {
        var result = new List<(string Mac, string? Ip, string? Hostname, string? Vendor)>();
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo("nmap", $"-sn -PR {subnet}")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            proc.Start();
            var output = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            // Parse nmap output
            string? currentIp = null;
            foreach (var line in output.Split('\n'))
            {
                var ipMatch = Regex.Match(line, @"Nmap scan report for (?:(\S+) \()?(\d+\.\d+\.\d+\.\d+)\)?");
                if (ipMatch.Success)
                {
                    currentIp = ipMatch.Groups[2].Value;
                }
                var macMatch = Regex.Match(line, @"MAC Address: ([0-9A-F:]+) \(([^)]+)\)", RegexOptions.IgnoreCase);
                if (macMatch.Success && currentIp is not null)
                {
                    result.Add((macMatch.Groups[1].Value.ToLower(), currentIp, null, macMatch.Groups[2].Value));
                    currentIp = null;
                }
            }
        }
        catch { /* nmap may not be installed */ }
        return result;
    }
}
