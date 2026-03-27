using Dapper;
using Lexi.Agent.Models;
using Npgsql;

namespace Lexi.Agent.Data.Repositories;

public class AnomalyRepository(NpgsqlDataSource db)
{
    public async Task<IEnumerable<AccessAnomalyRecord>> GetUnresolvedAsync()
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QueryAsync<AccessAnomalyRecord>(
            """
            SELECT id, source_ip, country_code, city, event_type, username, target_host,
                   event_count, first_seen, last_seen, is_resolved, resolved_at, notes
            FROM lexi_schema.access_anomalies
            WHERE is_resolved = false
            ORDER BY last_seen DESC
            """);
    }

    public async Task UpsertAsync(string? sourceIp, string eventType, string? username,
        string? targetHost, int eventCount, DateTimeOffset firstSeen, DateTimeOffset lastSeen,
        string? countryCode = null, string? city = null)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO lexi_schema.access_anomalies
                (source_ip, country_code, city, event_type, username, target_host, event_count, first_seen, last_seen)
            VALUES (@sourceIp, @countryCode, @city, @eventType, @username, @targetHost, @eventCount, @firstSeen, @lastSeen)
            ON CONFLICT (source_ip, event_type, username) DO UPDATE SET
                event_count  = EXCLUDED.event_count,
                last_seen    = EXCLUDED.last_seen,
                country_code = EXCLUDED.country_code,
                city         = EXCLUDED.city
            """,
            new { sourceIp, countryCode, city, eventType, username, targetHost, eventCount, firstSeen, lastSeen });
    }

    public async Task MarkResolvedAsync(Guid id, string? notes = null)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE lexi_schema.access_anomalies SET is_resolved = true, resolved_at = NOW(), notes = COALESCE(@notes, notes) WHERE id = @id",
            new { id, notes });
    }
}
