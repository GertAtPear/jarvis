using Mediahost.Agents.Data;
using System.Text.Json;
using Dapper;

namespace Andrew.Agent.Data.Repositories;

public class DiscoveryLogRepository(DbConnectionFactory db)
{
    public async Task LogAsync(Guid? serverId, string type, bool success, object details, int durationMs)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync(
            """
            INSERT INTO andrew_schema.discovery_log
                (server_id, discovery_type, status, details, duration_ms, ran_at)
            VALUES
                (@serverId, @type, @status, @details::jsonb, @durationMs, NOW())
            """,
            new
            {
                serverId,
                type,
                status = success ? "success" : "failure",
                details = JsonSerializer.Serialize(details),
                durationMs
            });
    }

    public async Task<IEnumerable<dynamic>> GetRecentAsync(int count = 20)
    {
        await using var conn = db.Create();
        return await conn.QueryAsync(
            """
            SELECT
                l.id,
                l.server_id     AS ServerId,
                s.hostname,
                l.discovery_type AS DiscoveryType,
                l.status,
                l.details,
                l.duration_ms   AS DurationMs,
                l.ran_at        AS RanAt
            FROM andrew_schema.discovery_log l
            LEFT JOIN andrew_schema.servers s ON s.id = l.server_id
            ORDER BY l.ran_at DESC
            LIMIT @count
            """,
            new { count });
    }
}
