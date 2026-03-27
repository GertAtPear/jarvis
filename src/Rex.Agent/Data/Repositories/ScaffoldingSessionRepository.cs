using System.Text.Json;
using Dapper;
using Mediahost.Agents.Data;

namespace Rex.Agent.Data.Repositories;

public class ScaffoldingSession
{
    public Guid     Id                { get; init; }
    public string?  AgentName         { get; init; }
    public string?  Department        { get; init; }
    public string?  Description       { get; init; }
    public string?  IntakeAnswers     { get; init; }
    public string?  ProposedTools     { get; init; }
    public int?     AssignedPort      { get; init; }
    public DateTime? PlanPresentedAt  { get; init; }
    public DateTime? ApprovedAt       { get; init; }
    public string   Status            { get; init; } = "intake";
    public DateTime CreatedAt         { get; init; }
    public DateTime UpdatedAt         { get; init; }
}

public class ScaffoldingSessionRepository(DbConnectionFactory db)
{
    public async Task<Guid> CreateSessionAsync(string description)
    {
        await using var conn = db.Create();
        return await conn.ExecuteScalarAsync<Guid>("""
            INSERT INTO rex_schema.scaffolding_sessions (description, status)
            VALUES (@description, 'intake')
            RETURNING id
            """, new { description });
    }

    public async Task UpdateIntakeAnswersAsync(Guid id, JsonDocument answers)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            UPDATE rex_schema.scaffolding_sessions
            SET intake_answers = @answers::jsonb, updated_at = NOW()
            WHERE id = @id
            """, new { id, answers = answers.RootElement.GetRawText() });
    }

    public async Task UpdateProposedToolsAsync(Guid id, JsonDocument tools)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            UPDATE rex_schema.scaffolding_sessions
            SET proposed_tools = @tools::jsonb, updated_at = NOW()
            WHERE id = @id
            """, new { id, tools = tools.RootElement.GetRawText() });
    }

    public async Task UpdateAssignedPortAsync(Guid id, int port)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            UPDATE rex_schema.scaffolding_sessions
            SET assigned_port = @port, updated_at = NOW()
            WHERE id = @id
            """, new { id, port });
    }

    public async Task SetPlanPresentedAsync(Guid id)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            UPDATE rex_schema.scaffolding_sessions
            SET status = 'planning', plan_presented_at = NOW(), updated_at = NOW()
            WHERE id = @id
            """, new { id });
    }

    public async Task ApproveAsync(Guid id)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            UPDATE rex_schema.scaffolding_sessions
            SET status = 'approved', approved_at = NOW(), updated_at = NOW()
            WHERE id = @id
            """, new { id });
    }

    public async Task UpdateStatusAsync(Guid id, string status)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            UPDATE rex_schema.scaffolding_sessions
            SET status = @status, updated_at = NOW()
            WHERE id = @id
            """, new { id, status });
    }

    public async Task UpdateAgentNameAsync(Guid id, string agentName, string? department = null)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            UPDATE rex_schema.scaffolding_sessions
            SET agent_name = @agentName, department = @department, updated_at = NOW()
            WHERE id = @id
            """, new { id, agentName, department });
    }

    public async Task<ScaffoldingSession?> GetByIdAsync(Guid id)
    {
        await using var conn = db.Create();
        return await conn.QueryFirstOrDefaultAsync<ScaffoldingSession>(
            "SELECT * FROM rex_schema.scaffolding_sessions WHERE id = @id", new { id });
    }

    public async Task<IEnumerable<ScaffoldingSession>> GetRecentAsync(int limit = 10)
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<ScaffoldingSession>("""
            SELECT * FROM rex_schema.scaffolding_sessions
            ORDER BY created_at DESC LIMIT @limit
            """, new { limit });
    }
}
