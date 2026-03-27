using Dapper;
using Jarvis.Api.Data;

namespace Jarvis.Api.Services;

public class MorningBriefingService(
    DbConnectionFactory db,
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<MorningBriefingService> logger)
{
    // SAST = UTC+2
    private static readonly TimeZoneInfo Sast =
        TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");

    public async Task<string?> GetBriefingIfNeededAsync(CancellationToken ct = default)
    {
        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Sast).Date;

        await using var conn = db.Create();

        // Check if already delivered today
        var existing = await conn.QuerySingleOrDefaultAsync<string>(
            "SELECT content FROM jarvis_schema.briefing_log WHERE briefing_date = @today",
            new { today });

        if (existing is not null)
            return null;   // already delivered today

        // Gather content
        var briefing = await CompileBriefingAsync(today, ct);

        if (string.IsNullOrWhiteSpace(briefing))
            return null;

        // Record in log so it only fires once today
        await conn.ExecuteAsync(
            "INSERT INTO jarvis_schema.briefing_log (briefing_date, content) VALUES (@today, @briefing)",
            new { today, briefing });

        return briefing;
    }

    private async Task<string> CompileBriefingAsync(DateTime today, CancellationToken ct)
    {
        var eveSection    = await FetchEveBriefingAsync(ct);
        var andrewSection = await FetchAndrewStatusAsync(ct);
        var rockySection  = await FetchRockyAlertsAsync(ct);
        var samSection    = await FetchSamStatusAsync(ct);
        var nadiaSection  = await FetchNadiaStatusAsync(ct);
        var lexiSection   = await FetchLexiStatusAsync(ct);

        var hour = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Sast).Hour;
        var greeting = hour < 12 ? "morning" : "afternoon";

        var dateStr = today.ToString("dddd, d MMMM yyyy");

        var parts = new List<string>
        {
            $"Good {greeting}, Gert. Here's your briefing for {dateStr}:"
        };

        if (!string.IsNullOrWhiteSpace(rockySection))
            parts.Add(rockySection);

        if (!string.IsNullOrWhiteSpace(samSection))
            parts.Add(samSection);

        if (!string.IsNullOrWhiteSpace(nadiaSection))
            parts.Add(nadiaSection);

        if (!string.IsNullOrWhiteSpace(lexiSection))
            parts.Add(lexiSection);

        if (!string.IsNullOrWhiteSpace(eveSection))
            parts.Add(eveSection);

        if (!string.IsNullOrWhiteSpace(andrewSection))
            parts.Add(andrewSection);

        return string.Join("\n\n", parts);
    }

    private async Task<string?> FetchEveBriefingAsync(CancellationToken ct)
    {
        var eveUrl = config["Agents:EveBaseUrl"] ?? "http://eve-agent:5003";
        try
        {
            var client = httpClientFactory.CreateClient("briefing-eve");
            client.BaseAddress = new Uri(eveUrl);
            client.Timeout = TimeSpan.FromSeconds(15);

            var response = await client.GetAsync("/api/eve/briefing/today", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not fetch Eve briefing from {Url}", eveUrl);
            return null;
        }
    }

    private async Task<string?> FetchAndrewStatusAsync(CancellationToken ct)
    {
        var andrewUrl = config["Agents:AndrewBaseUrl"] ?? "http://andrew-agent:5001";
        try
        {
            var client = httpClientFactory.CreateClient("briefing-andrew");
            client.BaseAddress = new Uri(andrewUrl);
            client.Timeout = TimeSpan.FromSeconds(15);

            var response = await client.GetAsync("/api/andrew/status", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not fetch Andrew status from {Url}", andrewUrl);
            return null;
        }
    }

    private async Task<string?> FetchSamStatusAsync(CancellationToken ct)
    {
        var samUrl = config["Agents:SamBaseUrl"] ?? "http://localhost:5007";
        try
        {
            var client = httpClientFactory.CreateClient("briefing-sam");
            client.Timeout = TimeSpan.FromSeconds(10);
            var response = await client.GetAsync($"{samUrl}/api/sam/status", ct);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            // Parse simple status JSON
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var total    = doc.RootElement.TryGetProperty("databases",  out var d) ? d.GetInt32() : 0;
            var healthy  = doc.RootElement.TryGetProperty("healthy",    out var h) ? h.GetInt32() : 0;
            var unhealthy = doc.RootElement.TryGetProperty("unhealthy", out var u) ? u.GetInt32() : 0;
            if (unhealthy == 0) return null; // no issues to surface
            return $"**Database Health (Sam):** {unhealthy}/{total} database(s) reporting errors.";
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not fetch Sam status from {Url}", samUrl);
            return null;
        }
    }

    private async Task<string?> FetchNadiaStatusAsync(CancellationToken ct)
    {
        var nadiaUrl = config["Agents:NadiaBaseUrl"] ?? "http://localhost:5008";
        try
        {
            var client = httpClientFactory.CreateClient("briefing-nadia");
            client.Timeout = TimeSpan.FromSeconds(10);
            var response = await client.GetAsync($"{nadiaUrl}/api/nadia/status", ct);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("recentFailovers", out var failovers)) return null;
            var count = failovers.GetArrayLength();
            if (count == 0) return null;
            return $"**Network (Nadia):** {count} recent failover event(s) detected.";
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not fetch Nadia status from {Url}", nadiaUrl);
            return null;
        }
    }

    private async Task<string?> FetchLexiStatusAsync(CancellationToken ct)
    {
        var lexiUrl = config["Agents:LexiBaseUrl"] ?? "http://localhost:5009";
        try
        {
            var client = httpClientFactory.CreateClient("briefing-lexi");
            client.Timeout = TimeSpan.FromSeconds(10);
            var response = await client.GetAsync($"{lexiUrl}/api/lexi/status", ct);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var expiring  = doc.RootElement.TryGetProperty("expiringCertsIn30Days", out var e) ? e.GetInt32() : 0;
            var anomalies = doc.RootElement.TryGetProperty("unresolvedAnomalies",   out var a) ? a.GetInt32() : 0;
            var cves      = doc.RootElement.TryGetProperty("criticalCves",          out var c) ? c.GetInt32() : 0;
            var unknown   = doc.RootElement.TryGetProperty("unknownDevices",        out var u) ? u.GetInt32() : 0;

            var issues = new List<string>();
            if (expiring  > 0) issues.Add($"{expiring} cert(s) expiring within 30 days");
            if (anomalies > 0) issues.Add($"{anomalies} unresolved access anomaly(ies)");
            if (cves      > 0) issues.Add($"{cves} critical CVE(s)");
            if (unknown   > 0) issues.Add($"{unknown} unknown device(s) on network");

            return issues.Count > 0
                ? $"**Security (Lexi):** {string.Join(", ", issues)}."
                : null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not fetch Lexi status from {Url}", lexiUrl);
            return null;
        }
    }

    private async Task<string?> FetchRockyAlertsAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = db.Create();

            // Unresolved alerts — any severity
            var unresolvedAlerts = (await conn.QueryAsync<(string DisplayName, string Severity, string Message, DateTimeOffset CreatedAt)>(
                """
                SELECT ws.display_name, ah.severity, ah.message, ah.created_at
                FROM rocky_schema.alert_history ah
                JOIN rocky_schema.watched_services ws ON ws.id = ah.service_id
                WHERE ah.resolved = false
                ORDER BY ah.created_at DESC
                LIMIT 20
                """)).ToList();

            // Services that had failures in the last 8 hours (overnight window)
            var recentFailures = (await conn.QueryAsync<(string DisplayName, int FailCount, DateTimeOffset LastFail)>(
                """
                SELECT ws.display_name,
                       COUNT(*) FILTER (WHERE cr.is_healthy = false) AS fail_count,
                       MAX(cr.checked_at) FILTER (WHERE cr.is_healthy = false) AS last_fail
                FROM rocky_schema.check_results cr
                JOIN rocky_schema.watched_services ws ON ws.id = cr.service_id
                WHERE cr.checked_at >= NOW() - INTERVAL '8 hours'
                  AND cr.is_healthy = false
                GROUP BY ws.display_name
                HAVING COUNT(*) FILTER (WHERE cr.is_healthy = false) > 0
                ORDER BY fail_count DESC
                """)).ToList();

            if (unresolvedAlerts.Count == 0 && recentFailures.Count == 0)
                return null;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("**Pipeline Monitor (Rocky):**");

            if (unresolvedAlerts.Count > 0)
            {
                sb.AppendLine($"- **{unresolvedAlerts.Count} unresolved alert(s):**");
                foreach (var a in unresolvedAlerts)
                    sb.AppendLine($"  - [{a.Severity.ToUpper()}] {a.DisplayName}: {a.Message}");
            }

            if (recentFailures.Count > 0)
            {
                sb.AppendLine("- Overnight failures (last 8 hours):");
                foreach (var f in recentFailures)
                    sb.AppendLine($"  - {f.DisplayName}: {f.FailCount} failed check(s), last at {TimeZoneInfo.ConvertTimeFromUtc(f.LastFail.UtcDateTime, Sast):HH:mm} SAST");
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not fetch Rocky alerts");
            return null;
        }
    }
}
