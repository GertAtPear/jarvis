using Dapper;
using Jarvis.Api.Data;

namespace Jarvis.Api.Services;

public class UsageAnalyticsService(DbConnectionFactory db)
{
    public async Task<UsageSummary> GetSummaryAsync(DateOnly from, DateOnly to, string? agentName = null)
    {
        await using var conn = db.Create();

        var sql = """
            SELECT
                COUNT(*)::INT                       AS TotalRequests,
                COALESCE(SUM(u.input_tokens),  0)::INT AS TotalInputTokens,
                COALESCE(SUM(u.output_tokens), 0)::INT AS TotalOutputTokens,
                COUNT(u.escalated_from)::INT        AS EscalationCount,
                COALESCE(SUM(
                    u.input_tokens  * COALESCE(r.input_cost_per_1k,  0) / 1000 +
                    u.output_tokens * COALESCE(r.output_cost_per_1k, 0) / 1000
                ), 0)                               AS EstimatedCostUsd
            FROM jarvis_schema.llm_usage u
            LEFT JOIN jarvis_schema.llm_cost_rates r
                ON r.provider_name = u.provider_name AND r.model_id = u.model_id
            WHERE u.created_at::date BETWEEN @from AND @to
              AND (@agentName IS NULL OR u.agent_name = @agentName)
            """;

        return await conn.QuerySingleAsync<UsageSummary>(sql, new { from, to, agentName });
    }

    public async Task<IEnumerable<DailyUsage>> GetDailyAsync(DateOnly from, DateOnly to)
    {
        await using var conn = db.Create();

        const string sql = """
            SELECT
                u.created_at::date              AS Date,
                u.provider_name                 AS Provider,
                COUNT(*)::INT                   AS Requests,
                COALESCE(SUM(u.input_tokens),  0)::INT AS InputTokens,
                COALESCE(SUM(u.output_tokens), 0)::INT AS OutputTokens
            FROM jarvis_schema.llm_usage u
            WHERE u.created_at::date BETWEEN @from AND @to
            GROUP BY u.created_at::date, u.provider_name
            ORDER BY u.created_at::date ASC
            """;

        return await conn.QueryAsync<DailyUsage>(sql, new { from, to });
    }

    public async Task<IEnumerable<AgentUsage>> GetByAgentAsync(DateOnly from, DateOnly to)
    {
        await using var conn = db.Create();

        const string sql = """
            SELECT
                u.agent_name                        AS AgentName,
                COUNT(*)::INT                       AS Requests,
                COALESCE(SUM(u.input_tokens),  0)::INT AS InputTokens,
                COALESCE(SUM(u.output_tokens), 0)::INT AS OutputTokens,
                COALESCE(SUM(
                    u.input_tokens  * COALESCE(r.input_cost_per_1k,  0) / 1000 +
                    u.output_tokens * COALESCE(r.output_cost_per_1k, 0) / 1000
                ), 0)                               AS EstimatedCostUsd
            FROM jarvis_schema.llm_usage u
            LEFT JOIN jarvis_schema.llm_cost_rates r
                ON r.provider_name = u.provider_name AND r.model_id = u.model_id
            WHERE u.created_at::date BETWEEN @from AND @to
            GROUP BY u.agent_name
            ORDER BY Requests DESC
            """;

        return await conn.QueryAsync<AgentUsage>(sql, new { from, to });
    }

    public async Task<IEnumerable<RoutingAnalytic>> GetRoutingAnalyticsAsync(DateOnly from, DateOnly to)
    {
        await using var conn = db.Create();

        const string sql = """
            SELECT
                u.rule_applied                      AS RuleName,
                u.model_id                          AS ModelId,
                u.provider_name                     AS Provider,
                COUNT(*)::INT                       AS Fires,
                ROUND(COUNT(*) * 100.0 / SUM(COUNT(*)) OVER(), 2)::FLOAT AS PctOfTotal
            FROM jarvis_schema.llm_usage u
            WHERE u.created_at::date BETWEEN @from AND @to
            GROUP BY u.rule_applied, u.model_id, u.provider_name
            ORDER BY Fires DESC
            """;

        return await conn.QueryAsync<RoutingAnalytic>(sql, new { from, to });
    }

    public async Task<IEnumerable<EscalationRecord>> GetEscalationsAsync(DateOnly from, DateOnly to, int limit = 50)
    {
        await using var conn = db.Create();

        const string sql = """
            SELECT
                u.id            AS Id,
                u.session_id    AS SessionId,
                u.agent_name    AS AgentName,
                u.model_id      AS ToModel,
                u.escalated_from AS FromModel,
                u.created_at    AS CreatedAt
            FROM jarvis_schema.llm_usage u
            WHERE u.escalated_from IS NOT NULL
              AND u.created_at::date BETWEEN @from AND @to
            ORDER BY u.created_at DESC
            LIMIT @limit
            """;

        return await conn.QueryAsync<EscalationRecord>(sql, new { from, to, limit });
    }

    public async Task<IEnumerable<SlowRequest>> GetSlowestAsync(int limit = 20)
    {
        await using var conn = db.Create();

        const string sql = """
            SELECT
                u.id            AS Id,
                u.agent_name    AS AgentName,
                u.model_id      AS ModelId,
                u.provider_name AS Provider,
                u.duration_ms   AS DurationMs,
                u.task_type     AS TaskType,
                u.created_at    AS CreatedAt
            FROM jarvis_schema.llm_usage u
            ORDER BY u.duration_ms DESC
            LIMIT @limit
            """;

        return await conn.QueryAsync<SlowRequest>(sql, new { limit });
    }

    public async Task<IEnumerable<CostRate>> GetCostRatesAsync()
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<CostRate>(
            "SELECT id, provider_name, model_id, input_cost_per_1k, output_cost_per_1k, effective_from FROM jarvis_schema.llm_cost_rates ORDER BY provider_name, model_id");
    }

    public async Task UpsertCostRateAsync(string providerName, string modelId,
        decimal inputCostPer1k, decimal outputCostPer1k)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync(
            """
            INSERT INTO jarvis_schema.llm_cost_rates (provider_name, model_id, input_cost_per_1k, output_cost_per_1k)
            VALUES (@providerName, @modelId, @inputCostPer1k, @outputCostPer1k)
            ON CONFLICT (provider_name, model_id) DO UPDATE SET
                input_cost_per_1k  = EXCLUDED.input_cost_per_1k,
                output_cost_per_1k = EXCLUDED.output_cost_per_1k,
                effective_from     = NOW()
            """,
            new { providerName, modelId, inputCostPer1k, outputCostPer1k });
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record UsageSummary(
    int     TotalRequests,
    int     TotalInputTokens,
    int     TotalOutputTokens,
    int     EscalationCount,
    decimal EstimatedCostUsd);

public record DailyUsage(
    DateOnly Date,
    string   Provider,
    int      Requests,
    int      InputTokens,
    int      OutputTokens);

public record AgentUsage(
    string  AgentName,
    int     Requests,
    int     InputTokens,
    int     OutputTokens,
    decimal EstimatedCostUsd);

public record RoutingAnalytic(
    string? RuleName,
    string  ModelId,
    string  Provider,
    int     Fires,
    decimal PctOfTotal);

public record EscalationRecord(
    Guid           Id,
    Guid?          SessionId,
    string         AgentName,
    string         ToModel,
    string         FromModel,
    DateTimeOffset CreatedAt);

public record SlowRequest(
    Guid           Id,
    string         AgentName,
    string         ModelId,
    string         Provider,
    int            DurationMs,
    string?        TaskType,
    DateTimeOffset CreatedAt);

public record CostRate(
    Guid    Id,
    string  ProviderName,
    string  ModelId,
    decimal InputCostPer1k,
    decimal OutputCostPer1k,
    DateTimeOffset EffectiveFrom);

public record UpsertCostRateRequest(
    string  ProviderName,
    string  ModelId,
    decimal InputCostPer1k,
    decimal OutputCostPer1k);
