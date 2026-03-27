using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Mediahost.Shared.Services;
using Npgsql;
using Sam.Agent.Data.Repositories;
using Sam.Agent.Models;

namespace Sam.Agent.Services;

/// <summary>
/// Scans a PostgreSQL database for connections, table stats, slow queries, and replication.
/// IMPORTANT: Never runs EXPLAIN ANALYZE — only plain EXPLAIN.
/// </summary>
public class PostgreSqlScanService(
    DatabaseRepository databaseRepo,
    ConnectionStatsRepository connRepo,
    TableStatsRepository tableRepo,
    SlowQueryRepository slowRepo,
    ReplicationRepository replRepo,
    DiscoveryLogRepository logRepo,
    IVaultService vault,
    ILogger<PostgreSqlScanService> logger)
{
    public async Task ScanAsync(DatabaseRecord db, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            Dictionary<string, string>? secret = null;
            if (db.VaultSecretPath is not null)
                secret = await vault.GetSecretsBulkAsync(db.VaultSecretPath, ct);

            var password = secret?.TryGetValue("password", out var p) == true ? p : "";
            var user     = secret?.TryGetValue("username", out var u) == true ? u : "postgres";

            var connStr = $"Host={db.Host};Port={db.Port};Database={db.DbName};Username={user};Password={password};Timeout=10;CommandTimeout=30";
            await using var dataSource = NpgsqlDataSource.Create(connStr);
            await using var conn = await dataSource.OpenConnectionAsync(ct);

            // Connection stats
            var activitySql = """
                SELECT
                    COUNT(*) FILTER (WHERE state = 'active')  AS active,
                    COUNT(*) FILTER (WHERE state = 'idle')    AS idle,
                    COUNT(*) FILTER (WHERE wait_event IS NOT NULL) AS waiting,
                    (SELECT setting::INT FROM pg_settings WHERE name = 'max_connections') AS max_conn
                FROM pg_stat_activity
                WHERE datname = current_database()
                """;
            var stats = await conn.QueryFirstAsync<(int active, int idle, int waiting, int max_conn)>(activitySql);
            await connRepo.InsertAsync(db.Id, stats.active, stats.max_conn, stats.idle, stats.waiting);

            // Table stats
            var tableStatsSql = """
                SELECT schemaname, relname, n_live_tup,
                       pg_relation_size(quote_ident(schemaname) || '.' || quote_ident(relname)) AS data_bytes,
                       pg_indexes_size(quote_ident(schemaname) || '.' || quote_ident(relname))  AS index_bytes
                FROM pg_stat_user_tables
                WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
                ORDER BY data_bytes DESC
                LIMIT 100
                """;
            var tableStat = await conn.QueryAsync<(string schemaname, string relname, long n_live_tup, long data_bytes, long index_bytes)>(tableStatsSql);
            await tableRepo.BulkUpsertAsync(db.Id, tableStat.Select(t => (t.schemaname, t.relname, t.n_live_tup, t.data_bytes, t.index_bytes)));

            // Slow queries (requires pg_stat_statements)
            try
            {
                var slowSql = """
                    SELECT query, mean_exec_time, max_exec_time, calls
                    FROM pg_stat_statements
                    WHERE mean_exec_time > 1000
                    ORDER BY mean_exec_time DESC
                    LIMIT 20
                    """;
                var slowQueries = await conn.QueryAsync<(string query, double mean_exec_time, double max_exec_time, int calls)>(slowSql);
                foreach (var q in slowQueries)
                {
                    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(q.query)))[..16];
                    await slowRepo.UpsertAsync(db.Id, hash, q.query, q.mean_exec_time, q.max_exec_time, q.calls, null);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "[Sam] pg_stat_statements not available for {Db}", db.Name);
            }

            // Replication
            try
            {
                var replSql = "SELECT client_addr, write_lag, flush_lag FROM pg_stat_replication LIMIT 1";
                var repl = await conn.QueryFirstOrDefaultAsync<(string? client_addr, TimeSpan? write_lag, TimeSpan? flush_lag)>(replSql);
                if (repl != default)
                    await replRepo.UpsertAsync(db.Id, "primary", repl.write_lag?.TotalSeconds, true, repl.client_addr);
                else
                {
                    var recovSql = "SELECT pg_is_in_recovery(), EXTRACT(EPOCH FROM (NOW() - pg_last_xact_replay_timestamp())) AS lag_sec";
                    var recov = await conn.QueryFirstAsync<(bool inRecovery, double? lag_sec)>(recovSql);
                    if (recov.inRecovery)
                        await replRepo.UpsertAsync(db.Id, "replica", recov.lag_sec, true, null);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "[Sam] Replication check failed for {Db}", db.Name);
            }

            await databaseRepo.UpdateStatusAsync(db.Id, "healthy");
            sw.Stop();
            await logRepo.InsertAsync(db.Id, "postgres_scan", "success", null, (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "[Sam] PostgreSQL scan failed for {Db}", db.Name);
            await databaseRepo.UpdateStatusAsync(db.Id, "error");
            await logRepo.InsertAsync(db.Id, "postgres_scan", "error", $"{{\"error\":\"{ex.Message.Replace("\"", "'")}\"}}",  (int)sw.ElapsedMilliseconds);
        }
    }
}
