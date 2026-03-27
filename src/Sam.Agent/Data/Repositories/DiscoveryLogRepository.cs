using Dapper;
using Npgsql;
using Sam.Agent.Models;

namespace Sam.Agent.Data.Repositories;

public class DiscoveryLogRepository(NpgsqlDataSource db)
{
    public async Task InsertAsync(Guid? databaseId, string scanType, string status, string? detailsJson, int durationMs)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "INSERT INTO sam_schema.discovery_log (database_id, scan_type, status, details, duration_ms) VALUES (@databaseId, @scanType, @status, @detailsJson::jsonb, @durationMs)",
            new { databaseId, scanType, status, detailsJson, durationMs });
    }
}
