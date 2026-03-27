using Dapper;
using Npgsql;
using Sam.Agent.Models;

namespace Sam.Agent.Data.Repositories;

public class ReplicationRepository(NpgsqlDataSource db)
{
    public async Task<ReplicationStatusRecord?> GetLatestAsync(Guid databaseId)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QueryFirstOrDefaultAsync<ReplicationStatusRecord>(
            "SELECT id, database_id, role, replication_lag_seconds, is_connected, replica_host, captured_at FROM sam_schema.replication_status WHERE database_id = @databaseId ORDER BY captured_at DESC LIMIT 1",
            new { databaseId });
    }

    public async Task UpsertAsync(Guid databaseId, string role, double? lagSeconds, bool isConnected, string? replicaHost)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO sam_schema.replication_status (database_id, role, replication_lag_seconds, is_connected, replica_host)
            VALUES (@databaseId, @role, @lagSeconds, @isConnected, @replicaHost)
            """,
            new { databaseId, role, lagSeconds, isConnected, replicaHost });
    }
}
