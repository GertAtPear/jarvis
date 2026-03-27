using System.Text.Json;
using Dapper;
using Mediahost.Agents.Data;
using Rex.Agent.Data.Repositories;

namespace Rex.Agent.Services;

public class AgentMetadataService(
    DbConnectionFactory db,
    AgentUpdateRepository updateRepo,
    ILogger<AgentMetadataService> logger)
{
    private static readonly HashSet<string> AllowedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "description", "routing_keywords", "department", "system_prompt_override",
        "display_name", "notes"
    };

    public async Task<object?> GetAgentInfoAsync(string agentName)
    {
        await using var conn = db.Create();

        var agent = await conn.QueryFirstOrDefaultAsync("""
            SELECT a.*, d.name as department_name,
                   p.port, p.is_active as port_active
            FROM jarvis_schema.agents a
            LEFT JOIN jarvis_schema.departments d ON d.id = a.department_id
            LEFT JOIN rex_schema.port_registry   p ON LOWER(p.agent_name) = LOWER(a.name)
            WHERE LOWER(a.name) = LOWER(@agentName)
            """, new { agentName });

        if (agent == null) return null;

        var scaffolded = await conn.QueryFirstOrDefaultAsync("""
            SELECT sa.* FROM rex_schema.scaffolded_agents sa
            WHERE LOWER(sa.agent_name) = LOWER(@agentName)
            ORDER BY sa.scaffolded_at DESC LIMIT 1
            """, new { agentName });

        var recentUpdates = await updateRepo.GetRecentAsync(agentName, 5);

        return new
        {
            agent,
            scaffolded_info = scaffolded,
            recent_updates  = recentUpdates
        };
    }

    public async Task<(bool Success, string Message)> UpdateMetadataAsync(
        string agentName, string field, string value, CancellationToken ct = default)
    {
        if (!AllowedFields.Contains(field))
            return (false, $"Field '{field}' is not updatable. Allowed: {string.Join(", ", AllowedFields)}");

        await using var conn = db.Create();

        // Read current value for audit log
        var current = await conn.ExecuteScalarAsync<string?>(
            $"SELECT {field} FROM jarvis_schema.agents WHERE LOWER(name) = LOWER(@agentName)",
            new { agentName });

        var affected = await conn.ExecuteAsync(
            $"""
            UPDATE jarvis_schema.agents
            SET {field} = @value, updated_at = NOW()
            WHERE LOWER(name) = LOWER(@agentName)
            """,
            new { value, agentName });

        if (affected == 0)
            return (false, $"Agent '{agentName}' not found.");

        var wasScaffolded = await conn.ExecuteScalarAsync<bool>(
            "SELECT was_scaffolded FROM jarvis_schema.agents WHERE LOWER(name) = LOWER(@agentName)",
            new { agentName });

        await updateRepo.LogAsync(new AgentUpdateRecord
        {
            AgentName     = agentName,
            Operation     = "metadata_update",
            WasScaffolded = wasScaffolded,
            Description   = $"Updated {field}: '{current}' → '{value}'",
            PerformedBy   = "rex",
            Success       = true,
        });

        logger.LogInformation("Updated {Field} for {Agent}", field, agentName);
        return (true, $"Updated {field} for {agentName}. Previous value: '{current}'.");
    }

    public async Task<IEnumerable<object>> ListAgentsAsync(bool includeRetired = false)
    {
        await using var conn = db.Create();

        var sql = includeRetired
            ? """
              SELECT a.name, a.display_name, a.status, a.was_scaffolded,
                     d.name as department, p.port, p.is_active as port_active,
                     a.retired_at, a.base_url, a.updated_at
              FROM jarvis_schema.agents a
              LEFT JOIN jarvis_schema.departments d ON d.id = a.department_id
              LEFT JOIN rex_schema.port_registry   p ON LOWER(p.agent_name) = LOWER(a.name)
              ORDER BY a.sort_order, a.name
              """
            : """
              SELECT a.name, a.display_name, a.status, a.was_scaffolded,
                     d.name as department, p.port, p.is_active as port_active,
                     a.retired_at, a.base_url, a.updated_at
              FROM jarvis_schema.agents a
              LEFT JOIN jarvis_schema.departments d ON d.id = a.department_id
              LEFT JOIN rex_schema.port_registry   p ON LOWER(p.agent_name) = LOWER(a.name)
              WHERE a.status != 'retired'
              ORDER BY a.sort_order, a.name
              """;

        var rows = await conn.QueryAsync(sql);
        return rows.Select(r => (object)r);
    }
}
