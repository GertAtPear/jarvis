using System.Net.Http.Json;
using System.Text.Json;

namespace Eve.Agent.Services;

/// <summary>
/// Posts calendar events to the Google Calendar MCP server.
/// MCP base URL is configured at GoogleCalendar:McpBaseUrl.
/// </summary>
public class GoogleCalendarService(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<GoogleCalendarService> logger)
{
    private readonly string _mcpBaseUrl =
        config["GoogleCalendar:McpBaseUrl"] ?? "http://localhost:3000";

    public async Task<string> CreateEventAsync(
        string title,
        string date,
        string? time,
        string? description,
        CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("google-calendar-mcp");
        client.BaseAddress = new Uri(_mcpBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(15);

        var payload = new
        {
            name = "create_event",
            arguments = new
            {
                summary     = title,
                start_date  = date,
                start_time  = time,
                description
            }
        };

        try
        {
            var response = await client.PostAsJsonAsync("/tools", payload, ct);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);

            // MCP response typically wraps in { result: { id: "..." } }
            if (doc.RootElement.TryGetProperty("result", out var result))
            {
                if (result.TryGetProperty("id", out var id))
                    return id.GetString() ?? Guid.NewGuid().ToString();
            }

            // Fallback: use the whole response as event ID
            return body.Length > 100 ? Guid.NewGuid().ToString() : body.Trim('"');
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Google Calendar MCP call failed — event not created in calendar");
            // Return a placeholder ID so the reminder is still marked as "calendar event requested"
            return $"pending-{Guid.NewGuid():N}";
        }
    }
}
