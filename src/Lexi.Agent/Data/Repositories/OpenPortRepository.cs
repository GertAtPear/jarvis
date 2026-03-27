using Dapper;
using Lexi.Agent.Models;
using Npgsql;

namespace Lexi.Agent.Data.Repositories;

public class OpenPortRepository(NpgsqlDataSource db)
{
    public async Task<IEnumerable<OpenPortRecord>> GetAllAsync(string? host = null, bool unexpectedOnly = false)
    {
        await using var conn = await db.OpenConnectionAsync();
        var sql = """
            SELECT id, server_id, host, port, protocol, service_name, state, is_expected, scanned_at
            FROM lexi_schema.open_ports
            WHERE (@host IS NULL OR host = @host)
              AND (@unexpectedOnly = false OR is_expected = false)
            ORDER BY host, port
            """;
        return await conn.QueryAsync<OpenPortRecord>(sql, new { host, unexpectedOnly });
    }

    public async Task UpsertAsync(string host, int port, string protocol, string? serviceName, string state, Guid? serverId = null)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO lexi_schema.open_ports (server_id, host, port, protocol, service_name, state)
            VALUES (@serverId, @host, @port, @protocol, @serviceName, @state)
            ON CONFLICT (host, port, protocol) DO UPDATE SET
                service_name = EXCLUDED.service_name,
                state        = EXCLUDED.state,
                scanned_at   = NOW()
            """,
            new { serverId, host, port, protocol, serviceName, state });
    }

    public async Task MarkExpectedAsync(Guid id)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE lexi_schema.open_ports SET is_expected = true WHERE id = @id", new { id });
    }
}
