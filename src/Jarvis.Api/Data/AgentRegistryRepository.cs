using Dapper;
using Jarvis.Api.Models;

namespace Jarvis.Api.Data;

public class AgentRegistryRepository(DbConnectionFactory db)
{
    private const string SelectColumns = """
        a.id,
        a.name,
        a.display_name          AS DisplayName,
        a.department_id         AS DepartmentId,
        d.name                  AS Department,
        a.description,
        a.system_prompt         AS SystemPrompt,
        a.base_url              AS BaseUrl,
        a.health_path           AS HealthPath,
        a.status,
        a.capabilities::text    AS CapabilitiesJson,
        a.routing_keywords::text AS RoutingKeywordsJson,
        a.sort_order            AS SortOrder,
        a.created_at            AS CreatedAt,
        a.updated_at            AS UpdatedAt
        """;

    private const string AgentsJoin = """
        FROM jarvis_schema.agents a
        LEFT JOIN jarvis_schema.departments d ON d.id = a.department_id
        """;

    public async Task<IEnumerable<AgentRecord>> GetActiveAgentsAsync()
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<AgentRecord>(
            $"SELECT {SelectColumns} {AgentsJoin} WHERE a.status = 'active' ORDER BY a.sort_order, a.name");
    }

    public async Task<AgentRecord?> GetByNameAsync(string name)
    {
        await using var conn = db.Create();
        return await conn.QuerySingleOrDefaultAsync<AgentRecord>(
            $"SELECT {SelectColumns} {AgentsJoin} WHERE a.name = @name",
            new { name });
    }

    public async Task<IEnumerable<AgentRecord>> GetByDepartmentAsync(string department)
    {
        await using var conn = db.Create();
        const string sql = """
            SELECT a.id, a.name, a.display_name AS DisplayName, a.department_id AS DepartmentId,
                   d.name AS Department,
                   a.description, a.system_prompt AS SystemPrompt, a.base_url AS BaseUrl,
                   a.health_path AS HealthPath, a.status,
                   a.capabilities::text AS CapabilitiesJson,
                   a.routing_keywords::text AS RoutingKeywordsJson,
                   a.sort_order AS SortOrder, a.created_at AS CreatedAt, a.updated_at AS UpdatedAt
            FROM jarvis_schema.agents a
            JOIN jarvis_schema.departments d ON d.id = a.department_id
            WHERE d.name = @department
            ORDER BY a.sort_order, a.name
            """;
        return await conn.QueryAsync<AgentRecord>(sql, new { department });
    }

    public async Task UpsertAgentAsync(AgentRecord agent)
    {
        await using var conn = db.Create();
        const string sql = """
            INSERT INTO jarvis_schema.agents
                (name, display_name, department_id, description, system_prompt,
                 base_url, health_path, status, capabilities, routing_keywords, sort_order)
            VALUES
                (@Name, @DisplayName, @DepartmentId, @Description, @SystemPrompt,
                 @BaseUrl, @HealthPath, @Status,
                 @CapabilitiesJson::jsonb, @RoutingKeywordsJson::jsonb, @SortOrder)
            ON CONFLICT (name) DO UPDATE SET
                display_name      = EXCLUDED.display_name,
                department_id     = EXCLUDED.department_id,
                description       = EXCLUDED.description,
                system_prompt     = EXCLUDED.system_prompt,
                base_url          = EXCLUDED.base_url,
                health_path       = EXCLUDED.health_path,
                status            = EXCLUDED.status,
                capabilities      = EXCLUDED.capabilities,
                routing_keywords  = EXCLUDED.routing_keywords,
                sort_order        = EXCLUDED.sort_order,
                updated_at        = NOW()
            """;
        await conn.ExecuteAsync(sql, new
        {
            agent.Name,
            agent.DisplayName,
            agent.DepartmentId,
            agent.Description,
            agent.SystemPrompt,
            agent.BaseUrl,
            agent.HealthPath,
            agent.Status,
            agent.CapabilitiesJson,
            agent.RoutingKeywordsJson,
            agent.SortOrder
        });
    }

    public async Task UpdateAgentStatusAsync(string name, string status)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync(
            "UPDATE jarvis_schema.agents SET status = @status, updated_at = NOW() WHERE name = @name",
            new { name, status });
    }

    public async Task<IEnumerable<AgentRecord>> GetAllAsync()
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<AgentRecord>(
            $"SELECT {SelectColumns} {AgentsJoin} ORDER BY a.sort_order, a.name");
    }
}
