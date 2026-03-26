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

        var hour = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Sast).Hour;
        var greeting = hour < 12 ? "morning" : "afternoon";

        var dateStr = today.ToString("dddd, d MMMM yyyy");

        var parts = new List<string>
        {
            $"Good {greeting}, Gert. Here's your briefing for {dateStr}:"
        };

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
}
