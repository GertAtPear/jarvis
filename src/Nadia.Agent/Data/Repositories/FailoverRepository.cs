using Dapper;
using Nadia.Agent.Models;
using Npgsql;

namespace Nadia.Agent.Data.Repositories;

public class FailoverRepository(NpgsqlDataSource db)
{
    public async Task InsertAsync(string? fromInterface, string? toInterface, string? triggerReason)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "INSERT INTO nadia_schema.failover_events (from_interface, to_interface, trigger_reason) VALUES (@fromInterface, @toInterface, @triggerReason)",
            new { fromInterface, toInterface, triggerReason });
    }

    public async Task<IEnumerable<FailoverEventRecord>> GetRecentAsync(int limit = 10)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QueryAsync<FailoverEventRecord>(
            "SELECT id, from_interface, to_interface, trigger_reason, detected_at, resolved_at, duration_seconds FROM nadia_schema.failover_events ORDER BY detected_at DESC LIMIT @limit",
            new { limit });
    }
}
