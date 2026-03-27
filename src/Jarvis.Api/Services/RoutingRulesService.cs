using Dapper;
using Jarvis.Api.Data;
using Mediahost.Llm.Services;

namespace Jarvis.Api.Services;

public class RoutingRulesService(
    DbConnectionFactory db,
    ModelSelectorService modelSelector)
{
    public async Task<IEnumerable<RoutingRuleDto>> GetAllAsync()
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<RoutingRuleDto>(
            """
            SELECT id, rule_name, priority, needs_vision, needs_long_ctx, complexity, task_type,
                   agent_name, provider_name, model_id, reason, is_active, created_at, updated_at
            FROM jarvis_schema.model_routing_rules
            ORDER BY priority ASC
            """);
    }

    public async Task<RoutingRuleDto> CreateAsync(CreateRoutingRuleRequest req)
    {
        await using var conn = db.Create();
        var id = await conn.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO jarvis_schema.model_routing_rules
                (rule_name, priority, needs_vision, needs_long_ctx, complexity, task_type,
                 agent_name, provider_name, model_id, reason, is_active)
            VALUES
                (@ruleName, @priority, @needsVision, @needsLongCtx, @complexity, @taskType,
                 @agentName, @providerName, @modelId, @reason, @isActive)
            RETURNING id
            """,
            new
            {
                ruleName    = req.RuleName,
                priority    = req.Priority,
                needsVision = req.NeedsVision,
                needsLongCtx = req.NeedsLongCtx,
                complexity  = req.Complexity,
                taskType    = req.TaskType,
                agentName   = req.AgentName,
                providerName = req.ProviderName,
                modelId     = req.ModelId,
                reason      = req.Reason,
                isActive    = req.IsActive ?? true
            });

        modelSelector.InvalidateCache();
        return (await GetAllAsync()).First(r => r.Id == id);
    }

    public async Task<bool> UpdateAsync(Guid id, CreateRoutingRuleRequest req)
    {
        await using var conn = db.Create();
        var rows = await conn.ExecuteAsync(
            """
            UPDATE jarvis_schema.model_routing_rules SET
                rule_name     = @ruleName,
                priority      = @priority,
                needs_vision  = @needsVision,
                needs_long_ctx = @needsLongCtx,
                complexity    = @complexity,
                task_type     = @taskType,
                agent_name    = @agentName,
                provider_name = @providerName,
                model_id      = @modelId,
                reason        = @reason,
                is_active     = @isActive,
                updated_at    = NOW()
            WHERE id = @id
            """,
            new
            {
                id,
                ruleName    = req.RuleName,
                priority    = req.Priority,
                needsVision = req.NeedsVision,
                needsLongCtx = req.NeedsLongCtx,
                complexity  = req.Complexity,
                taskType    = req.TaskType,
                agentName   = req.AgentName,
                providerName = req.ProviderName,
                modelId     = req.ModelId,
                reason      = req.Reason,
                isActive    = req.IsActive ?? true
            });

        modelSelector.InvalidateCache();
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        await using var conn = db.Create();
        var rows = await conn.ExecuteAsync(
            "DELETE FROM jarvis_schema.model_routing_rules WHERE id = @id", new { id });
        modelSelector.InvalidateCache();
        return rows > 0;
    }

    public void InvalidateCache() => modelSelector.InvalidateCache();
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record RoutingRuleDto(
    Guid    Id,
    string  RuleName,
    int     Priority,
    bool?   NeedsVision,
    bool?   NeedsLongCtx,
    string? Complexity,
    string? TaskType,
    string? AgentName,
    string  ProviderName,
    string  ModelId,
    string? Reason,
    bool    IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CreateRoutingRuleRequest(
    string  RuleName,
    int     Priority,
    bool?   NeedsVision,
    bool?   NeedsLongCtx,
    string? Complexity,
    string? TaskType,
    string? AgentName,
    string  ProviderName,
    string  ModelId,
    string? Reason,
    bool?   IsActive);
