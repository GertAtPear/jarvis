using System.Diagnostics;
using Mediahost.Llm.Models;
using Mediahost.Llm.Providers;
using Microsoft.Extensions.Logging;

namespace Mediahost.Llm.Services;

public sealed class LlmService(
    TaskClassifierService classifier,
    ModelSelectorService selector,
    IEnumerable<ILlmProvider> providers,
    LlmUsageLogger usageLogger,
    ILogger<LlmService> logger)
{
    private readonly Dictionary<string, ILlmProvider> _providers =
        providers.ToDictionary(p => p.ProviderName, StringComparer.OrdinalIgnoreCase);

    public async Task<LlmServiceResponse> CompleteAsync(
        string agentName,
        LlmRequest request,
        Guid? sessionId = null,
        CancellationToken ct = default)
    {
        // 1 & 2. Extract user text and attachments for classification
        var userText    = ExtractUserText(request.Messages);
        var attachments = ExtractAttachments(request.Messages);

        // 3. Classify the task
        var classification = await classifier.ClassifyAsync(userText, agentName, attachments, ct);

        // 4. Select model candidates (primary + fallbacks, one per provider)
        var candidates = await selector.SelectModelsAsync(classification, ct);

        // 5. Try each candidate until one succeeds
        LlmResponse? response = null;
        ModelContext? selectedModel = null;
        var sw = Stopwatch.StartNew();

        foreach (var modelCtx in candidates)
        {
            if (!_providers.TryGetValue(modelCtx.Provider, out var provider))
            {
                logger.LogWarning("LLM provider '{Provider}' is not registered — skipping.",
                    modelCtx.Provider);
                continue;
            }

            var effectiveRequest = request.MaxTokens.HasValue
                ? request
                : request with { MaxTokens = modelCtx.MaxTokens };

            try
            {
                response = await provider.CompleteAsync(modelCtx.Model, effectiveRequest, ct);
                selectedModel = modelCtx;
                break;
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                logger.LogWarning(ex,
                    "LLM transient error: agent={Agent} provider={Provider} model={Model} — trying next fallback.",
                    agentName, modelCtx.Provider, modelCtx.Model);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "LLM non-transient error: agent={Agent} provider={Provider} model={Model} — trying next fallback.",
                    agentName, modelCtx.Provider, modelCtx.Model);
            }
        }

        sw.Stop();

        if (response is null || selectedModel is null)
            throw new InvalidOperationException(
                $"All LLM providers failed for agent '{agentName}'. " +
                $"Tried: {string.Join(", ", candidates.Select(c => c.Provider))}");

        // 6. Fire-and-forget usage log
        _ = Task.Run(async () => await usageLogger.LogAsync(
            agentName,
            selectedModel.Provider,
            selectedModel.Model,
            classification.TaskType,
            selectedModel.RuleApplied,
            response.Usage.InputTokens,
            response.Usage.OutputTokens,
            (int)sw.ElapsedMilliseconds,
            sessionId), CancellationToken.None);

        // 7. Return
        return new LlmServiceResponse(response, selectedModel);
    }

    /// <summary>
    /// Returns true for errors where switching to another provider is worth trying:
    /// rate limits (HTTP 429), Anthropic overload (HTTP 529), and provider-specific
    /// rate-limit exception types.
    /// </summary>
    private static bool IsTransient(Exception ex) =>
        // Rate limits and overload — standard API errors
        ex.GetType().Name.Contains("RateLimits", StringComparison.OrdinalIgnoreCase) ||
        ex.GetType().Name.Contains("Overloaded", StringComparison.OrdinalIgnoreCase) ||
        (ex is HttpRequestException http && ((int?)http.StatusCode is 429 or 529)) ||
        // API key not configured — treat as "skip this provider" so the chain continues
        (ex is InvalidOperationException ioe && ioe.Message.Contains("not found in vault"));

    private static string ExtractUserText(IReadOnlyList<LlmMessage> messages)
    {
        var parts = messages
            .Where(m => m.Role == "user")
            .SelectMany(m => m.Content.OfType<TextContent>())
            .Select(t => t.Text);
        return string.Join(" ", parts);
    }

    private static IReadOnlyList<LlmContent>? ExtractAttachments(IReadOnlyList<LlmMessage> messages)
    {
        var attachments = messages
            .SelectMany(m => m.Content)
            .Where(c => c is ImageContent or DocumentContent)
            .ToList();
        return attachments.Count > 0 ? attachments : null;
    }
}

public record LlmServiceResponse(LlmResponse Response, ModelContext ModelUsed);
