using Dapper;
using Npgsql;
using Sam.Agent.Models;

namespace Sam.Agent.Data.Repositories;

public class ConnectionStatsRepository(NpgsqlDataSource db)
{
    public async Task InsertAsync(Guid databaseId, int active, int max, int idle, int waiting)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "INSERT INTO sam_schema.connection_stats (database_id, active_connections, max_connections, idle_connections, waiting_queries) VALUES (@databaseId, @active, @max, @idle, @waiting)",
            new { databaseId, active, max, idle, waiting });
    }

    public async Task<ConnectionStatsRecord?> GetLatestAsync(Guid databaseId)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QueryFirstOrDefaultAsync<ConnectionStatsRecord>(
            "SELECT id, database_id, active_connections, max_connections, idle_connections, waiting_queries, captured_at FROM sam_schema.connection_stats WHERE database_id = @databaseId ORDER BY captured_at DESC LIMIT 1",
            new { databaseId });
    }
}
