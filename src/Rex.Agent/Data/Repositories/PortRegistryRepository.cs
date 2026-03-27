using Dapper;
using Mediahost.Agents.Data;

namespace Rex.Agent.Data.Repositories;

public class PortRegistryEntry
{
    public int      Port        { get; init; }
    public string   AgentName   { get; init; } = "";
    public DateTime ReservedAt  { get; init; }
    public bool     IsActive    { get; init; }
    public string?  Notes       { get; init; }
}

public class PortRegistryRepository(DbConnectionFactory db)
{
    private const int PortFloor = 5010;

    public async Task<int> AssignNextPortAsync(string agentName)
    {
        await using var conn = db.Create();

        // Get max port across ALL ports (active or not), floor at PortFloor-1 so next is at least PortFloor
        var maxPort = await conn.ExecuteScalarAsync<int?>(
            "SELECT MAX(port) FROM rex_schema.port_registry") ?? (PortFloor - 1);

        var nextPort = Math.Max(maxPort + 1, PortFloor);

        await conn.ExecuteAsync("""
            INSERT INTO rex_schema.port_registry (port, agent_name, is_active)
            VALUES (@nextPort, @agentName, true)
            """, new { nextPort, agentName });

        return nextPort;
    }

    public async Task<IEnumerable<PortRegistryEntry>> GetAllAsync()
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<PortRegistryEntry>(
            "SELECT * FROM rex_schema.port_registry ORDER BY port");
    }

    public async Task DeactivateAsync(int port)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            UPDATE rex_schema.port_registry SET is_active = false WHERE port = @port
            """, new { port });
    }
}
