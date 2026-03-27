using Dapper;
using Npgsql;
using Sam.Agent.Models;

namespace Sam.Agent.Data.Repositories;

public class TableStatsRepository(NpgsqlDataSource db)
{
    public async Task<IEnumerable<TableStatsRecord>> GetTopBySizeAsync(Guid databaseId, int limit = 20)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QueryAsync<TableStatsRecord>(
            """
            SELECT id, database_id, schema_name, table_name, row_estimate, data_size_bytes, index_size_bytes, captured_at
            FROM sam_schema.table_stats
            WHERE database_id = @databaseId
            ORDER BY data_size_bytes DESC
            LIMIT @limit
            """,
            new { databaseId, limit });
    }

    public async Task BulkUpsertAsync(Guid databaseId, IEnumerable<(string Schema, string Table, long Rows, long DataBytes, long IndexBytes)> stats)
    {
        await using var conn = await db.OpenConnectionAsync();
        foreach (var s in stats)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO sam_schema.table_stats (database_id, schema_name, table_name, row_estimate, data_size_bytes, index_size_bytes)
                VALUES (@databaseId, @schema, @table, @rows, @dataBytes, @indexBytes)
                ON CONFLICT (database_id, schema_name, table_name) DO UPDATE SET
                    row_estimate    = EXCLUDED.row_estimate,
                    data_size_bytes = EXCLUDED.data_size_bytes,
                    index_size_bytes = EXCLUDED.index_size_bytes,
                    captured_at     = NOW()
                """,
                new { databaseId, schema = s.Schema, table = s.Table, rows = s.Rows, dataBytes = s.DataBytes, indexBytes = s.IndexBytes });
        }
    }
}
