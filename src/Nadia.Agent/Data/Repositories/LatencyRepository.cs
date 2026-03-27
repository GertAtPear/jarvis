using Dapper;
using Nadia.Agent.Models;
using Npgsql;

namespace Nadia.Agent.Data.Repositories;

public class LatencyRepository(NpgsqlDataSource db)
{
    public async Task InsertAsync(Guid interfaceId, string targetHost, double rttMs, double packetLossPct)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "INSERT INTO nadia_schema.latency_history (interface_id, target_host, rtt_ms, packet_loss_pct) VALUES (@interfaceId, @targetHost, @rttMs, @packetLossPct)",
            new { interfaceId, targetHost, rttMs, packetLossPct });
    }

    public async Task<IEnumerable<LatencyHistoryRecord>> GetHistoryAsync(Guid interfaceId, int hours = 24)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QueryAsync<LatencyHistoryRecord>(
            """
            SELECT id, interface_id, target_host, rtt_ms, packet_loss_pct, probed_at
            FROM nadia_schema.latency_history
            WHERE interface_id = @interfaceId AND probed_at > NOW() - INTERVAL '1 hour' * @hours
            ORDER BY probed_at DESC
            """,
            new { interfaceId, hours });
    }

    public async Task<double?> GetAverageRttAsync(Guid interfaceId, int hours = 1)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<double?>(
            "SELECT AVG(rtt_ms) FROM nadia_schema.latency_history WHERE interface_id = @interfaceId AND probed_at > NOW() - INTERVAL '1 hour' * @hours",
            new { interfaceId, hours });
    }
}
