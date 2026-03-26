using System.Text.Json;
using Jarvis.Api.Data;
using Jarvis.Api.Models;
using Mediahost.Llm.Models;

namespace Jarvis.Api.Services;

/// <summary>
/// Generates LLM tool definitions dynamically from the agent registry,
/// so adding a new agent to the database automatically makes it routable
/// with no code changes.
/// </summary>
public class DynamicRoutingService(AgentRegistryRepository registry, ILogger<DynamicRoutingService> logger)
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Builds a ToolDefinition for each active agent.
    /// Tool name: ask_{agent.name}
    /// Tool description: agent.description + routing keywords hint
    /// Input schema: { question: string }
    /// </summary>
    public async Task<IReadOnlyList<ToolDefinition>> BuildToolsAsync(CancellationToken ct = default)
    {
        var agents = await registry.GetActiveAgentsAsync();
        var tools  = new List<ToolDefinition>();

        foreach (var agent in agents)
        {
            if (string.IsNullOrEmpty(agent.BaseUrl))
                continue;   // no endpoint — skip

            var keywords = agent.RoutingKeywords;
            var keywordHint = keywords.Count > 0
                ? $" Keywords: {string.Join(", ", keywords)}."
                : "";

            var schemaJson = JsonDocument.Parse("""
                {
                  "type": "object",
                  "properties": {
                    "question": {
                      "type": "string",
                      "description": "The question or task to send to this agent."
                    }
                  },
                  "required": ["question"]
                }
                """);

            tools.Add(new ToolDefinition(
                Name: $"ask_{agent.Name}",
                Description: $"{agent.Description}.{keywordHint}",
                InputSchema: schemaJson
            ));
        }

        logger.LogDebug("Built {Count} dynamic tools from agent registry", tools.Count);
        return tools;
    }

    /// <summary>
    /// Given a tool name like "ask_andrew", returns the matching AgentRecord (or null).
    /// </summary>
    public async Task<AgentRecord?> ResolveAgentFromToolAsync(string toolName, CancellationToken ct = default)
    {
        // tool name is "ask_{agent.name}"
        if (!toolName.StartsWith("ask_", StringComparison.OrdinalIgnoreCase))
            return null;

        var agentName = toolName["ask_".Length..];
        return await registry.GetByNameAsync(agentName);
    }

    /// <summary>
    /// Extracts the "question" parameter from a tool_use input JsonDocument.
    /// </summary>
    public static string ExtractQuestion(JsonDocument input)
    {
        if (input.RootElement.TryGetProperty("question", out var q))
            return q.GetString() ?? "";
        return input.RootElement.GetRawText();
    }
}
