using System.Text.Json;
using Dapper;
using Mediahost.Agents.Data;

namespace Rex.Agent.Data.Repositories;

public class ScaffoldedAgentLog
{
    public Guid     Id                  { get; init; }
    public Guid?    SessionId           { get; init; }
    public string   AgentName           { get; init; } = "";
    public int?     Port                { get; init; }
    public string?  Department          { get; init; }
    public string?  FilesCreated        { get; init; }
    public bool     SchemaCreated       { get; init; }
    public bool     ComposePatched      { get; init; }
    public bool     BuildSuccess        { get; init; }
    public bool     HealthCheckPassed   { get; init; }
    public bool     RegisteredInJarvis  { get; init; }
    public string?  SmokeTestResponse   { get; init; }
    public string?  ErrorDetails        { get; init; }
    public DateTime ScaffoldedAt        { get; init; }
}

public class ScaffoldedAgentRepository(DbConnectionFactory db)
{
    public async Task<Guid> LogAsync(ScaffoldedAgentLog entry)
    {
        await using var conn = db.Create();
        return await conn.ExecuteScalarAsync<Guid>("""
            INSERT INTO rex_schema.scaffolded_agents
                (session_id, agent_name, port, department, files_created, schema_created,
                 compose_patched, build_success, health_check_passed, registered_in_jarvis,
                 smoke_test_response, error_details)
            VALUES
                (@SessionId, @AgentName, @Port, @Department, @FilesCreated::jsonb, @SchemaCreated,
                 @ComposePatched, @BuildSuccess, @HealthCheckPassed, @RegisteredInJarvis,
                 @SmokeTestResponse, @ErrorDetails)
            RETURNING id
            """, entry);
    }

    public async Task UpdateBuildResultAsync(
        Guid id,
        bool buildSuccess,
        bool healthPassed,
        bool registered,
        string? smokeTestResponse,
        string? errorDetails)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            UPDATE rex_schema.scaffolded_agents
            SET build_success        = @buildSuccess,
                health_check_passed  = @healthPassed,
                registered_in_jarvis = @registered,
                smoke_test_response  = @smokeTestResponse,
                error_details        = @errorDetails
            WHERE id = @id
            """, new { id, buildSuccess, healthPassed, registered, smokeTestResponse, errorDetails });
    }

    public async Task<IEnumerable<ScaffoldedAgentLog>> GetRecentAsync(int limit = 10)
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<ScaffoldedAgentLog>("""
            SELECT * FROM rex_schema.scaffolded_agents
            ORDER BY scaffolded_at DESC LIMIT @limit
            """, new { limit });
    }
}
