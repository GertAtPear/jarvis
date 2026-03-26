using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Mediahost.Llm.Services;

public sealed class LlmUsageLogger(
    NpgsqlDataSource db,
    ILogger<LlmUsageLogger> logger)
{
    public async Task LogAsync(
        string agentName,
        string provider,
        string model,
        string? taskType,
        string? ruleApplied,
        int inputTokens,
        int outputTokens,
        int durationMs,
        Guid? sessionId)
    {
        try
        {
            await using var conn = await db.OpenConnectionAsync();

            const string sql = """
                INSERT INTO jarvis_schema.llm_usage
                    (session_id, agent_name, provider_name, model_id,
                     task_type, input_tokens, output_tokens, duration_ms, rule_applied)
                VALUES
                    (@sessionId, @agentName, @provider, @model,
                     @taskType, @inputTokens, @outputTokens, @durationMs, @ruleApplied)
                """;

            await conn.ExecuteAsync(sql, new
            {
                sessionId,
                agentName,
                provider,
                model,
                taskType,
                inputTokens,
                outputTokens,
                durationMs,
                ruleApplied
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to log LLM usage for agent={Agent} provider={Provider} model={Model}.",
                agentName, provider, model);
        }
    }

    public async Task<LlmUsageSummary> GetSummaryAsync(
        string? agentName, DateOnly from, DateOnly to)
    {
        await using var conn = await db.OpenConnectionAsync();

        const string sql = """
            SELECT
                COUNT(*)::INT                      AS TotalRequests,
                COALESCE(SUM(input_tokens),0)::INT  AS TotalInputTokens,
                COALESCE(SUM(output_tokens),0)::INT AS TotalOutputTokens
            FROM jarvis_schema.llm_usage
            WHERE created_at::date BETWEEN @from AND @to
              AND (@agentName IS NULL OR agent_name = @agentName)
            """;

        var totals = await conn.QuerySingleAsync<(int TotalRequests, int TotalInputTokens, int TotalOutputTokens)>(
            sql, new { from, to, agentName });

        const string byModelSql = """
            SELECT
                provider_name                       AS Provider,
                model_id                            AS Model,
                COUNT(*)::INT                       AS Requests,
                COALESCE(SUM(input_tokens),0)::INT  AS InputTokens,
                COALESCE(SUM(output_tokens),0)::INT AS OutputTokens
            FROM jarvis_schema.llm_usage
            WHERE created_at::date BETWEEN @from AND @to
              AND (@agentName IS NULL OR agent_name = @agentName)
            GROUP BY provider_name, model_id
            ORDER BY Requests DESC
            """;

        var byModel = (await conn.QueryAsync<AgentModelUsage>(byModelSql, new { from, to, agentName }))
            .ToList();

        return new LlmUsageSummary(
            totals.TotalRequests,
            totals.TotalInputTokens,
            totals.TotalOutputTokens,
            byModel);
    }
}

public record LlmUsageSummary(
    int TotalRequests,
    int TotalInputTokens,
    int TotalOutputTokens,
    IReadOnlyList<AgentModelUsage> ByModel);

public record AgentModelUsage(
    string Provider,
    string Model,
    int Requests,
    int InputTokens,
    int OutputTokens);
