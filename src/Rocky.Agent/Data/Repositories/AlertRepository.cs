using Dapper;
using Rocky.Agent.Data;
using Rocky.Agent.Models;

namespace Rocky.Agent.Data.Repositories;

public class AlertRepository(DbConnectionFactory db)
{
    public async Task<Guid> InsertAsync(AlertRecord alert)
    {
        await using var conn = db.Create();
        return await conn.QuerySingleAsync<Guid>("""
            INSERT INTO rocky_schema.alert_history
                (service_id, severity, message, resolved, created_at)
            VALUES
                (@ServiceId, @Severity, @Message, @Resolved, NOW())
            RETURNING id
            """, alert);
    }

    public async Task<IEnumerable<AlertRecord>> GetUnresolvedAsync()
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<AlertRecord>("""
            SELECT id, service_id AS ServiceId, severity, message, resolved,
                   resolved_at AS ResolvedAt, created_at AS CreatedAt
            FROM rocky_schema.alert_history
            WHERE resolved = false
            ORDER BY created_at DESC
            """);
    }

    public async Task<IEnumerable<AlertRecord>> GetRecentAsync(int limit = 50)
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<AlertRecord>("""
            SELECT id, service_id AS ServiceId, severity, message, resolved,
                   resolved_at AS ResolvedAt, created_at AS CreatedAt
            FROM rocky_schema.alert_history
            ORDER BY created_at DESC
            LIMIT @limit
            """, new { limit });
    }

    public async Task ResolveAsync(Guid serviceId)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            UPDATE rocky_schema.alert_history
            SET resolved = true, resolved_at = NOW()
            WHERE service_id = @serviceId AND resolved = false
            """, new { serviceId });
    }

    public async Task<bool> HasUnresolvedAsync(Guid serviceId)
    {
        await using var conn = db.Create();
        return await conn.QuerySingleAsync<bool>("""
            SELECT EXISTS (
                SELECT 1 FROM rocky_schema.alert_history
                WHERE service_id = @serviceId AND resolved = false
            )
            """, new { serviceId });
    }
}
