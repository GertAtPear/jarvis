using System.Text.Json;
using System.Text.RegularExpressions;
using Mediahost.Agents.Data;
using Mediahost.Llm.Models;
using Mediahost.Llm.Services;
using Microsoft.Extensions.Logging;

namespace Mediahost.Agents.Services;

/// <summary>
/// Abstract base for all agents. Provides the full agentic LLM loop with
/// tool execution, permanent memory, session history, and shared web tools.
///
/// Subclasses must set <see cref="AgentName"/>, <see cref="BaseSystemPrompt"/>,
/// and optionally override <see cref="MaxTokens"/> and
/// <see cref="LoadAdditionalContextAsync"/> (e.g. Eve's daily reminder context).
/// </summary>
public abstract class BaseAgentService(
    LlmService llm,
    IAgentToolExecutor executor,
    IAgentMemoryService memory,
    ILogger logger) : IAgentService
{
    protected abstract string AgentName { get; }
    protected abstract string BaseSystemPrompt { get; }
    protected virtual int MaxTokens => 4096;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static readonly IReadOnlyList<ToolDefinition> SharedTools =
    [
        new ToolDefinition(
            "web_search",
            "Search the internet for current information, documentation, pricing, news, or any topic. " +
            "Use this whenever you need up-to-date information or facts you are not certain about.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "query": {
                  "type": "string",
                  "description": "The search query"
                },
                "count": {
                  "type": "number",
                  "description": "Number of results to return (default: 5, max: 10)"
                }
              },
              "required": ["query"]
            }
            """)),

        new ToolDefinition(
            "fetch_page",
            "Fetch and read the content of a specific web page or URL. " +
            "Use after web_search to read documentation, articles, or pages in full.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "url": {
                  "type": "string",
                  "description": "The full URL to fetch"
                }
              },
              "required": ["url"]
            }
            """))
    ];

    /// <summary>
    /// Override to append agent-specific context to the system prompt.
    /// Called once per <see cref="HandleMessageAsync"/> invocation.
    /// Return null to skip.
    /// </summary>
    protected virtual Task<string?> LoadAdditionalContextAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult<string?>(null);

    public async Task<AgentResponse> HandleMessageAsync(
        string message, Guid? sessionId, CancellationToken ct = default)
    {
        var sid = sessionId ?? Guid.NewGuid();

        await memory.EnsureSessionAsync(sid, ct);
        var history = await memory.LoadHistoryAsync(sid, ct);
        var facts   = await memory.LoadFactsAsync(ct);

        history.Add(new LlmMessage("user", [new TextContent(message)]));

        var agentTools  = executor.GetTools();
        var allTools    = agentTools.Concat(SharedTools).ToList();
        var toolCallCount = 0;
        string? finalResponse = null;

        // Build system prompt: base + permanent facts + any agent-specific context
        var systemPrompt = BaseSystemPrompt;
        if (facts.Count > 0)
            systemPrompt += "\n\n## Permanent Memory\n" +
                            string.Join("\n", facts.Select(kv => $"- {kv.Key}: {kv.Value}"));

        var additionalContext = await LoadAdditionalContextAsync(sid, ct);
        if (additionalContext is not null)
            systemPrompt += "\n\n" + additionalContext;

        while (true)
        {
            var request = new LlmRequest(
                SystemPrompt: systemPrompt,
                Messages: history.ToList(),
                Tools: allTools,
                MaxTokens: MaxTokens
            );

            LlmResponse response;
            try
            {
                response = (await llm.CompleteAsync(AgentName, request, sid, ct)).Response;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{Agent}] LLM call failed for session {Session}", AgentName, sid);
                return new AgentResponse(
                    "I'm temporarily unavailable. Please try again in a moment.",
                    sid, toolCallCount);
            }

            var assistantContent = new List<LlmContent>();
            if (!string.IsNullOrEmpty(response.TextContent))
                assistantContent.Add(new TextContent(response.TextContent));
            foreach (var tu in response.ToolUses)
                assistantContent.Add(tu);

            if (assistantContent.Count > 0)
                history.Add(new LlmMessage("assistant", assistantContent));

            if (response.StopReason != StopReason.ToolUse || response.ToolUses.Count == 0)
            {
                finalResponse = response.TextContent ?? "";
                break;
            }

            var toolResults = new List<LlmContent>();
            foreach (var toolUse in response.ToolUses)
            {
                toolCallCount++;
                logger.LogDebug("[{Agent}] Calling tool {Tool} (session {Session})", AgentName, toolUse.Name, sid);

                string result;
                try
                {
                    result = toolUse.Name switch
                    {
                        // web_search is handled natively by the LLM provider (Anthropic/Gemini).
                        // This branch is a fallback that should rarely be reached.
                        "web_search" => "Web search is handled by the AI provider natively. " +
                                        "Use fetch_page to read a specific URL.",
                        "fetch_page" => await FetchPageAsync(toolUse.Input, ct),
                        _            => await executor.ExecuteAsync(toolUse.Name, toolUse.Input, ct)
                    };
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[{Agent}] Tool {Tool} threw for session {Session}", AgentName, toolUse.Name, sid);
                    result = $"{{\"error\": \"{ex.Message.Replace("\"", "'")}\"}}";
                }
                toolResults.Add(new ToolResultContent(toolUse.Id, result, ToolName: toolUse.Name));
            }

            history.Add(new LlmMessage("tool_result", toolResults));
        }

        if (!string.IsNullOrWhiteSpace(finalResponse))
        {
            try { await memory.SaveTurnAsync(sid, message, finalResponse, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "[{Agent}] Failed to save turn for session {Session}", AgentName, sid); }
        }

        return new AgentResponse(finalResponse ?? "", sid, toolCallCount);
    }

    private static async Task<string> FetchPageAsync(JsonDocument input, CancellationToken ct)
    {
        var url = input.RootElement.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(url))
            return "{\"error\": \"url is required\"}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; MediahostAI/1.0; research bot)");

            var response = await Http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return $"{{\"error\": \"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}\"}}";

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                const int maxRaw = 10_000;
                return body.Length > maxRaw ? body[..maxRaw] + "\n...[truncated]" : body;
            }

            var text = StripHtml(body);
            const int maxHtml = 10_000;
            return text.Length > maxHtml ? text[..maxHtml] + "\n...[truncated]" : text;
        }
        catch (Exception ex)
        {
            return $"{{\"error\": \"Failed to fetch page: {ex.Message.Replace("\"", "'")}\"}}";
        }
    }

    private static string StripHtml(string html)
    {
        html = Regex.Replace(html, @"<(script|style)[^>]*>[\s\S]*?<\/\1>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<[^>]+>", " ");
        html = html.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
                   .Replace("&quot;", "\"").Replace("&#39;", "'").Replace("&nbsp;", " ");
        return Regex.Replace(html, @"\s{2,}", " ").Trim();
    }
}
