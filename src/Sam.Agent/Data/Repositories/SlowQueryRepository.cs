using Dapper;
using Npgsql;
using Sam.Agent.Models;

namespace Sam.Agent.Data.Repositories;

public class SlowQueryRepository(NpgsqlDataSource db)
{
    public async Task<IEnumerable<SlowQueryRecord>> GetRecentAsync(Guid databaseId, int limit = 20)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QueryAsync<SlowQueryRecord>(
            """
            SELECT id, database_id, query_hash, query_text, avg_duration_ms, max_duration_ms,
                   execution_count, schema_name, captured_at
            FROM sam_schema.slow_query_log
            WHERE database_id = @databaseId
            ORDER BY avg_duration_ms DESC
            LIMIT @limit
            """,
            new { databaseId, limit });
    }

    public async Task UpsertAsync(Guid databaseId, string queryHash, string queryText,
        double avgDurationMs, double maxDurationMs, int executionCount, string? schemaName)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO sam_schema.slow_query_log
                (database_id, query_hash, query_text, avg_duration_ms, max_duration_ms, execution_count, schema_name)
            VALUES (@databaseId, @queryHash, @queryText, @avgDurationMs, @maxDurationMs, @executionCount, @schemaName)
            ON CONFLICT (database_id, query_hash) DO UPDATE SET
                avg_duration_ms  = EXCLUDED.avg_duration_ms,
                max_duration_ms  = EXCLUDED.max_duration_ms,
                execution_count  = EXCLUDED.execution_count,
                captured_at      = NOW()
            """,
            new { databaseId, queryHash, queryText, avgDurationMs, maxDurationMs, executionCount, schemaName });
    }
}
