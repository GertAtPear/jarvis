using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mediahost.Llm.Models;
using Mediahost.Llm.Providers;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Mediahost.Llm.Services;

public sealed class TaskClassifierService(
    AnthropicProvider anthropic,
    IConnectionMultiplexer redis,
    ILogger<TaskClassifierService> logger)
{
    private const string ClassifierModel = "claude-haiku-4-5";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private static readonly string SystemPrompt =
        """
        You are a task classifier. Analyse the user message and respond ONLY with JSON:
        {
          "complexity": "simple|moderate|complex",
          "task_type": "lookup|analysis|code|writing|briefing|tool_use",
          "needs_vision": true/false,
          "needs_long_context": true/false,
          "reason": "one sentence explanation"
        }
        - simple: single-fact lookup or status check
        - moderate: multi-step reasoning but bounded
        - complex: deep analysis, large code changes, extensive writing
        - needs_long_context: true if conversation/document exceeds ~50k tokens
        """;

    public async Task<TaskClassification> ClassifyAsync(
        string userMessage,
        string agentName,
        IReadOnlyList<LlmContent>? attachments,
        CancellationToken ct)
    {
        try
        {
            // Force flags from attachments before hitting the cache
            var forceVision      = attachments?.Any(a => a is ImageContent) ?? false;
            var forceLongContext  = false; // documents > 50k not detectable here without parsing

            var cacheKey = $"classify:{ComputeHash(agentName + userMessage)}";
            var db = redis.GetDatabase();

            var cached = await db.StringGetAsync(cacheKey);
            if (cached.HasValue)
            {
                var hit = Deserialize(cached!, agentName);
                // Override with forced flags even on cache hit
                if (forceVision || forceLongContext)
                    hit = hit with
                    {
                        NeedsVision      = hit.NeedsVision      || forceVision,
                        NeedsLongContext = hit.NeedsLongContext  || forceLongContext
                    };
                return hit;
            }

            var request = new LlmRequest(
                SystemPrompt: SystemPrompt,
                Messages: [new LlmMessage("user", [new TextContent(userMessage)])],
                MaxTokens: 256,
                Temperature: 0f);

            var response = await anthropic.CompleteAsync(ClassifierModel, request, ct);

            var classification = ParseResponse(response.TextContent, agentName);
            classification = classification with
            {
                NeedsVision      = classification.NeedsVision      || forceVision,
                NeedsLongContext = classification.NeedsLongContext  || forceLongContext
            };

            await db.StringSetAsync(cacheKey, Serialize(classification), CacheTtl);

            return classification;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Classification failed for agent {Agent}; using fallback.", agentName);
            return Fallback(agentName);
        }
    }

    private static TaskClassification ParseResponse(string? json, string agentName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Fallback(agentName);

        try
        {
            // Strip markdown code fences if present
            var raw = json.Trim();
            if (raw.StartsWith("```")) raw = raw[raw.IndexOf('\n')..].TrimStart();
            if (raw.EndsWith("```")) raw = raw[..raw.LastIndexOf("```")].TrimEnd();

            var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            return new TaskClassification(
                Complexity:            root.GetProperty("complexity").GetString() ?? "moderate",
                TaskType:              root.GetProperty("task_type").GetString()  ?? "tool_use",
                NeedsVision:           root.GetProperty("needs_vision").GetBoolean(),
                NeedsLongContext:      root.GetProperty("needs_long_context").GetBoolean(),
                AgentName:             agentName,
                ClassificationReason:  root.GetProperty("reason").GetString()     ?? string.Empty);
        }
        catch
        {
            return Fallback(agentName);
        }
    }

    private static TaskClassification Fallback(string agentName) =>
        new("moderate", "tool_use", false, false, agentName, "classification fallback");

    private static string Serialize(TaskClassification c) =>
        JsonSerializer.Serialize(c);

    private static TaskClassification Deserialize(string json, string agentName)
    {
        try { return JsonSerializer.Deserialize<TaskClassification>(json)!; }
        catch { return Fallback(agentName); }
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
