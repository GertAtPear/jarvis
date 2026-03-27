using System.Text.Json;
using Dapper;
using Mediahost.Agents.Data;

namespace Rex.Agent.Data.Repositories;

public class AgentUpdateRecord
{
    public Guid     Id              { get; init; }
    public string   AgentName       { get; init; } = "";
    public string   Operation       { get; init; } = "";  // metadata_update|code_update|soft_retire|hard_retire|reactivate
    public bool     WasScaffolded   { get; init; }
    public string?  Description     { get; init; }
    public string?  FilesModified   { get; init; }
    public bool     SchemaDropped   { get; init; }
    public string?  PerformedBy     { get; init; }
    public DateTime PerformedAt     { get; init; }
    public bool     Success         { get; init; } = true;
    public string?  ErrorDetails    { get; init; }
}

public class AgentUpdateRepository(DbConnectionFactory db)
{
    public async Task<Guid> LogAsync(AgentUpdateRecord record)
    {
        await using var conn = db.Create();
        return await conn.ExecuteScalarAsync<Guid>("""
            INSERT INTO rex_schema.agent_updates
                (agent_name, operation, was_scaffolded, description, files_modified,
                 schema_dropped, performed_by, success, error_details)
            VALUES
                (@AgentName, @Operation, @WasScaffolded, @Description, @FilesModified::jsonb,
                 @SchemaDropped, @PerformedBy, @Success, @ErrorDetails)
            RETURNING id
            """, record);
    }

    public async Task<IEnumerable<AgentUpdateRecord>> GetRecentAsync(string agentName, int limit = 5)
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<AgentUpdateRecord>("""
            SELECT * FROM rex_schema.agent_updates
            WHERE agent_name = @agentName
            ORDER BY performed_at DESC LIMIT @limit
            """, new { agentName, limit });
    }
}
