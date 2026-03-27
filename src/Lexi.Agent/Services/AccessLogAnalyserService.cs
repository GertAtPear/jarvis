using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Lexi.Agent.Data.Repositories;
using Mediahost.Agents.Capabilities;
using Mediahost.Tools.Models;
using Npgsql;

namespace Lexi.Agent.Services;

public partial class AccessLogAnalyserService(
    AnomalyRepository anomalyRepo,
    ScanLogRepository scanLogRepo,
    SshCapability ssh,
    NpgsqlDataSource db,
    ILogger<AccessLogAnalyserService> logger)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task AnalyseAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var totalFindings = 0;

        try
        {
            // Get servers from Andrew's registry (handle gracefully if empty)
            List<(string hostname, string? ip)> servers;
            try
            {
                await using var conn = await db.OpenConnectionAsync(ct);
                servers = (await conn.QueryAsync<(string hostname, string? ip)>(
                    "SELECT hostname, ip_address FROM andrew_schema.servers WHERE is_active = true LIMIT 50")).ToList();
            }
            catch
            {
                servers = [];
            }

            foreach (var server in servers)
            {
                var host = server.ip ?? server.hostname;
                try
                {
                    var target = new ConnectionTarget(server.hostname, host, 22);
                    var creds  = await ssh.GetCredentialsAsync(server.hostname, ct);
                    if (creds is null)
                    {
                        logger.LogDebug("[Lexi] No SSH credentials found for {Host}", server.hostname);
                        continue;
                    }

                    var result = await ssh.RunAndReadAsync(
                        target, creds,
                        "grep 'Failed password\\|Accepted' /var/log/auth.log 2>/dev/null | tail -5000",
                        ct: ct);

                    if (result is null) continue;

                    var anomalies = ParseAuthLog(result, server.hostname);
                    totalFindings += anomalies.Count;

                    // Get geolocation for IPs with many failures
                    var highRiskIps = anomalies
                        .Where(a => a.EventCount > 5 && a.SourceIp is not null)
                        .Select(a => a.SourceIp!)
                        .Distinct()
                        .Take(100)
                        .ToList();

                    var geoData = await GetGeoDataAsync(highRiskIps, ct);

                    foreach (var a in anomalies)
                    {
                        var geo = a.SourceIp is not null && geoData.TryGetValue(a.SourceIp, out var g) ? g : (null, null);
                        await anomalyRepo.UpsertAsync(
                            a.SourceIp, a.EventType, a.Username, server.hostname,
                            a.EventCount, a.FirstSeen, a.LastSeen,
                            geo.Item1, geo.Item2);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "[Lexi] Access log analysis failed for {Host}", host);
                }
            }

            sw.Stop();
            await scanLogRepo.InsertAsync("access_log", "success", servers.Count, totalFindings, (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "[Lexi] AccessLogAnalyserService failed");
            await scanLogRepo.InsertAsync("access_log", "error", 0, 0, (int)sw.ElapsedMilliseconds);
        }
    }

    private static List<(string? SourceIp, string EventType, string? Username, int EventCount, DateTimeOffset FirstSeen, DateTimeOffset LastSeen)>
        ParseAuthLog(string logContent, string targetHost)
    {
        var failures = new Dictionary<string, (string? Ip, string? User, DateTimeOffset First, DateTimeOffset Last, int Count)>();

        foreach (var line in logContent.Split('\n'))
        {
            var failMatch = FailedPasswordRegex().Match(line);
            if (failMatch.Success)
            {
                var ip   = failMatch.Groups["ip"].Value;
                var user = failMatch.Groups["user"].Value;
                var key  = $"fail:{ip}:{user}";
                var ts   = DateTimeOffset.UtcNow; // simplified — real impl would parse timestamp

                if (failures.TryGetValue(key, out var existing))
                    failures[key] = (ip, user, existing.First, ts, existing.Count + 1);
                else
                    failures[key] = (ip, user, ts, ts, 1);
            }
        }

        return failures.Values
            .Where(f => f.Count >= 3)
            .Select(f => (f.Ip, "failed_password", f.User, f.Count, f.First, f.Last))
            .ToList();
    }

    private static async Task<Dictionary<string, (string? Country, string? City)>> GetGeoDataAsync(
        List<string> ips, CancellationToken ct)
    {
        if (ips.Count == 0) return [];
        try
        {
            var body = JsonSerializer.Serialize(ips.Select(ip => new { query = ip }));
            using var req = new HttpRequestMessage(HttpMethod.Post, "http://ip-api.com/batch");
            req.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return [];

            var json = await resp.Content.ReadAsStringAsync(ct);
            var results = JsonSerializer.Deserialize<List<JsonElement>>(json) ?? [];
            return results
                .Where(r => r.TryGetProperty("query", out _))
                .ToDictionary(
                    r => r.GetProperty("query").GetString() ?? "",
                    r => (
                        r.TryGetProperty("country", out var c) ? c.GetString() : null,
                        r.TryGetProperty("city",    out var ci) ? ci.GetString() : null
                    ));
        }
        catch { return []; }
    }

    [GeneratedRegex(@"Failed password for (?:invalid user )?(?<user>\S+) from (?<ip>\S+)")]
    private static partial Regex FailedPasswordRegex();
}
