using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Jarvis.Api.Models;

namespace Jarvis.Api.Services;

public class AgentClientFactory(IHttpClientFactory httpClientFactory, ILogger<AgentClientFactory> logger)
{
    private readonly ConcurrentDictionary<string, HttpClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    private HttpClient GetClient(AgentRecord agent)
    {
        return _clients.GetOrAdd(agent.Name, _ =>
        {
            var client = httpClientFactory.CreateClient($"agent-{agent.Name}");
            client.BaseAddress = new Uri(agent.BaseUrl!);
            client.Timeout = TimeSpan.FromSeconds(120);
            return client;
        });
    }

    public async Task<string> SendMessageAsync(AgentRecord agent, string message, Guid sessionId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(agent.BaseUrl))
            throw new InvalidOperationException($"Agent '{agent.Name}' has no BaseUrl configured.");

        var client = GetClient(agent);

        var payload = new { message, sessionId };

        try
        {
            var response = await client.PostAsJsonAsync(
                $"/api/{agent.Name}/chat", payload, ct);

            response.EnsureSuccessStatusCode();

            // Agents may return { response: "..." } or plain text
            var body = await response.Content.ReadAsStringAsync(ct);

            if (body.TrimStart().StartsWith('{'))
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("response", out var prop))
                    return prop.GetString() ?? body;
            }

            return body;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to contact agent {Agent} at {BaseUrl}", agent.Name, agent.BaseUrl);
            return $"[{agent.DisplayName} is unavailable: {ex.Message}]";
        }
    }

    public async Task<string?> HealthCheckAsync(AgentRecord agent, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(agent.BaseUrl))
            return null;

        try
        {
            var client = GetClient(agent);
            var response = await client.GetAsync(agent.HealthPath, ct);
            return response.IsSuccessStatusCode ? "healthy" : "unhealthy";
        }
        catch
        {
            return "offline";
        }
    }
}
