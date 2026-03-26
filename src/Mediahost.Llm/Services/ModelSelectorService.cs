using Dapper;
using Mediahost.Llm.Models;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Mediahost.Llm.Services;

public sealed class ModelSelectorService(
    NpgsqlDataSource db,
    ILogger<ModelSelectorService> logger)
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    private List<RoutingRule>? _cachedRules;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<ModelContext> SelectModelAsync(
        TaskClassification classification, CancellationToken ct)
    {
        var candidates = await SelectModelsAsync(classification, ct);
        return candidates[0];
    }

    /// <summary>
    /// Returns up to 3 model candidates in priority order, deduplicated by provider.
    /// Use this for fallback: try candidates[0] first, then candidates[1], etc.
    /// </summary>
    public async Task<List<ModelContext>> SelectModelsAsync(
        TaskClassification classification, CancellationToken ct)
    {
        var rules = await GetRulesAsync(ct);
        var seenProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ModelContext>(3);

        foreach (var rule in rules)
        {
            if (!Matches(rule, classification)) continue;
            if (!seenProviders.Add(rule.ProviderName)) continue; // one candidate per provider

            result.Add(new ModelContext(
                Provider:        rule.ProviderName,
                Model:           rule.ModelId,
                SelectionReason: rule.Reason ?? $"Matched rule: {rule.RuleName}",
                RuleApplied:     rule.RuleName,
                MaxTokens:       rule.MaxOutputTokens,
                EscalateAsync:   _ => throw new NotSupportedException(
                                      "Model escalation is a Phase 3 feature.")));

            if (result.Count >= 3) break;
        }

        if (result.Count == 0)
        {
            logger.LogWarning("No routing rule matched for {Agent}/{Complexity}/{TaskType}. Using built-in defaults.",
                classification.AgentName, classification.Complexity, classification.TaskType);

            result.Add(new ModelContext("anthropic", "claude-sonnet-4-6",
                "Default fallback — no rule matched", null, 16000,
                _ => throw new NotSupportedException("Model escalation is a Phase 3 feature.")));
            result.Add(new ModelContext("google", "gemini-2.0-flash",
                "Built-in fallback 1", null, 8000,
                _ => throw new NotSupportedException("Model escalation is a Phase 3 feature.")));
            result.Add(new ModelContext("openai", "gpt-4o",
                "Built-in fallback 2", null, 4096,
                _ => throw new NotSupportedException("Model escalation is a Phase 3 feature.")));
        }

        return result;
    }

    private static bool Matches(RoutingRule rule, TaskClassification c)
    {
        if (rule.NeedsVision.HasValue     && rule.NeedsVision     != c.NeedsVision)      return false;
        if (rule.NeedsLongCtx.HasValue    && rule.NeedsLongCtx    != c.NeedsLongContext)  return false;
        if (rule.Complexity  is not null  && rule.Complexity       != c.Complexity)       return false;
        if (rule.TaskType    is not null  && rule.TaskType         != c.TaskType)         return false;
        if (rule.AgentName   is not null  && rule.AgentName        != c.AgentName)        return false;
        return true;
    }

    private async Task<List<RoutingRule>> GetRulesAsync(CancellationToken ct)
    {
        if (_cachedRules is not null && DateTime.UtcNow < _cacheExpiry)
            return _cachedRules;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cachedRules is not null && DateTime.UtcNow < _cacheExpiry)
                return _cachedRules;

            await using var conn = await db.OpenConnectionAsync(ct);

            const string sql = """
                SELECT
                    r.rule_name      AS RuleName,
                    r.priority       AS Priority,
                    r.needs_vision   AS NeedsVision,
                    r.needs_long_ctx AS NeedsLongCtx,
                    r.complexity     AS Complexity,
                    r.task_type      AS TaskType,
                    r.agent_name     AS AgentName,
                    r.provider_name  AS ProviderName,
                    r.model_id       AS ModelId,
                    r.reason         AS Reason,
                    COALESCE(m.max_output_tokens, 4096) AS MaxOutputTokens
                FROM jarvis_schema.model_routing_rules r
                LEFT JOIN jarvis_schema.llm_models m
                    ON m.model_id = r.model_id
                WHERE r.is_active = true
                ORDER BY r.priority ASC
                """;

            var rules = (await conn.QueryAsync<RoutingRule>(sql)).AsList();

            _cachedRules = rules;
            _cacheExpiry = DateTime.UtcNow.Add(CacheLifetime);

            logger.LogInformation("Loaded {Count} model routing rules.", rules.Count);
            return rules;
        }
        finally
        {
            _lock.Release();
        }
    }

    private sealed class RoutingRule
    {
        public string  RuleName        { get; init; } = null!;
        public int     Priority        { get; init; }
        public bool?   NeedsVision     { get; init; }
        public bool?   NeedsLongCtx    { get; init; }
        public string? Complexity      { get; init; }
        public string? TaskType        { get; init; }
        public string? AgentName       { get; init; }
        public string  ProviderName    { get; init; } = null!;
        public string  ModelId         { get; init; } = null!;
        public string? Reason          { get; init; }
        public int     MaxOutputTokens { get; init; }
    }
}
