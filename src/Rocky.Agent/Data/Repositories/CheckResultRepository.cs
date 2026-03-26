using Dapper;
using Rocky.Agent.Data;
using Rocky.Agent.Models;

namespace Rocky.Agent.Data.Repositories;

public class CheckResultRepository(DbConnectionFactory db)
{
    public async Task InsertAsync(CheckResult result)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            INSERT INTO rocky_schema.check_results
                (id, service_id, is_healthy, detail, duration_ms, checked_at)
            VALUES
                (@Id, @ServiceId, @IsHealthy, @Detail, @DurationMs, @CheckedAt)
            """, result);
    }

    public async Task<CheckResult?> GetLatestAsync(Guid serviceId)
    {
        await using var conn = db.Create();
        return await conn.QuerySingleOrDefaultAsync<CheckResult>("""
            SELECT id, service_id AS ServiceId, is_healthy AS IsHealthy,
                   detail, duration_ms AS DurationMs, checked_at AS CheckedAt
            FROM rocky_schema.check_results
            WHERE service_id = @serviceId
            ORDER BY checked_at DESC
            LIMIT 1
            """, new { serviceId });
    }

    public async Task<IEnumerable<CheckResult>> GetRecentAsync(Guid serviceId, int limit = 50)
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<CheckResult>("""
            SELECT id, service_id AS ServiceId, is_healthy AS IsHealthy,
                   detail, duration_ms AS DurationMs, checked_at AS CheckedAt
            FROM rocky_schema.check_results
            WHERE service_id = @serviceId
            ORDER BY checked_at DESC
            LIMIT @limit
            """, new { serviceId, limit });
    }

    public async Task DeleteOlderThanAsync(DateTime cutoff)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync(
            "DELETE FROM rocky_schema.check_results WHERE checked_at < @cutoff",
            new { cutoff });
    }
}
