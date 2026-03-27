using Dapper;
using Nadia.Agent.Models;
using Npgsql;

namespace Nadia.Agent.Data.Repositories;

public class DnsCheckRepository(NpgsqlDataSource db)
{
    public async Task InsertAsync(string resolverHost, string recordType, string queryName,
        string? resolvedValue, int resolutionMs, bool isHealthy)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "INSERT INTO nadia_schema.dns_checks (resolver_host, record_type, query_name, resolved_value, resolution_ms, is_healthy) VALUES (@resolverHost, @recordType, @queryName, @resolvedValue, @resolutionMs, @isHealthy)",
            new { resolverHost, recordType, queryName, resolvedValue, resolutionMs, isHealthy });
    }

    public async Task<IEnumerable<DnsCheckRecord>> GetRecentAsync(int limit = 20)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QueryAsync<DnsCheckRecord>(
            "SELECT id, resolver_host, record_type, query_name, resolved_value, resolution_ms, is_healthy, checked_at FROM nadia_schema.dns_checks ORDER BY checked_at DESC LIMIT @limit",
            new { limit });
    }
}
