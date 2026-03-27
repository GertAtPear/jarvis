using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Mediahost.Shared.Services;
using MySqlConnector;
using Sam.Agent.Data.Repositories;
using Sam.Agent.Models;

namespace Sam.Agent.Services;

/// <summary>
/// Scans a MySQL/MariaDB database. IMPORTANT: Never runs EXPLAIN ANALYZE — only plain EXPLAIN.
/// </summary>
public class MySqlScanService(
    DatabaseRepository databaseRepo,
    ConnectionStatsRepository connRepo,
    TableStatsRepository tableRepo,
    SlowQueryRepository slowRepo,
    ReplicationRepository replRepo,
    DiscoveryLogRepository logRepo,
    IVaultService vault,
    ILogger<MySqlScanService> logger)
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
            var user     = secret?.TryGetValue("username", out var u) == true ? u : "root";

            var connStr = $"Server={db.Host};Port={db.Port};Database={db.DbName};User={user};Password={password};ConnectionTimeout=10;DefaultCommandTimeout=30";
            await using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync(ct);

            // Connection stats
            var statusSql = "SHOW STATUS WHERE Variable_name IN ('Threads_connected','Threads_running','Max_used_connections') UNION SELECT 'max_connections', @@max_connections";
            var rows = (await conn.QueryAsync<(string Variable_name, string Value)>(statusSql)).ToDictionary(r => r.Variable_name, r => r.Value);
            var active  = rows.TryGetValue("Threads_running", out var tr) ? int.Parse(tr) : 0;
            var total   = rows.TryGetValue("Threads_connected", out var tc) ? int.Parse(tc) : 0;
            var maxConn = rows.TryGetValue("max_connections",   out var mc) ? int.Parse(mc) : 0;
            await connRepo.InsertAsync(db.Id, active, maxConn, total - active, 0);

            // Table stats
            var tableStatsSql = """
                SELECT TABLE_SCHEMA, TABLE_NAME,
                       COALESCE(TABLE_ROWS, 0) AS rows_est,
                       COALESCE(DATA_LENGTH, 0) AS data_bytes,
                       COALESCE(INDEX_LENGTH, 0) AS index_bytes
                FROM information_schema.TABLES
                WHERE TABLE_TYPE = 'BASE TABLE'
                  AND TABLE_SCHEMA NOT IN ('information_schema','performance_schema','mysql','sys')
                ORDER BY data_bytes DESC
                LIMIT 100
                """;
            var tableStat = await conn.QueryAsync<(string TABLE_SCHEMA, string TABLE_NAME, long rows_est, long data_bytes, long index_bytes)>(tableStatsSql);
            await tableRepo.BulkUpsertAsync(db.Id, tableStat.Select(t => (t.TABLE_SCHEMA, t.TABLE_NAME, t.rows_est, t.data_bytes, t.index_bytes)));

            // Slow queries from performance_schema
            try
            {
                var slowSql = """
                    SELECT DIGEST_TEXT, AVG_TIMER_WAIT/1e9 AS avg_ms, MAX_TIMER_WAIT/1e9 AS max_ms, COUNT_STAR AS calls
                    FROM performance_schema.events_statements_summary_by_digest
                    WHERE AVG_TIMER_WAIT > 1e9
                    ORDER BY AVG_TIMER_WAIT DESC
                    LIMIT 20
                    """;
                var slowQueries = await conn.QueryAsync<(string DIGEST_TEXT, double avg_ms, double max_ms, int calls)>(slowSql);
                foreach (var q in slowQueries)
                {
                    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(q.DIGEST_TEXT ?? "")))[..16];
                    await slowRepo.UpsertAsync(db.Id, hash, q.DIGEST_TEXT ?? "", q.avg_ms, q.max_ms, q.calls, null);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "[Sam] performance_schema not available for {Db}", db.Name);
            }

            // Replication
            try
            {
                var replSql = "SHOW REPLICA STATUS";
                var repl = await conn.QueryFirstOrDefaultAsync<dynamic>(replSql);
                if (repl != null)
                {
                    double? lagSec = repl.Seconds_Behind_Source;
                    await replRepo.UpsertAsync(db.Id, "replica", lagSec, true, null);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "[Sam] Replication check failed for {Db}", db.Name);
            }

            await databaseRepo.UpdateStatusAsync(db.Id, "healthy");
            sw.Stop();
            await logRepo.InsertAsync(db.Id, "mysql_scan", "success", null, (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "[Sam] MySQL scan failed for {Db}", db.Name);
            await databaseRepo.UpdateStatusAsync(db.Id, "error");
            await logRepo.InsertAsync(db.Id, "mysql_scan", "error", $"{{\"error\":\"{ex.Message.Replace("\"", "'")}\"}}",  (int)sw.ElapsedMilliseconds);
        }
    }
}
