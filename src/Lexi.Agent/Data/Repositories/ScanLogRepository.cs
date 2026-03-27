using Dapper;
using Npgsql;

namespace Lexi.Agent.Data.Repositories;

public class ScanLogRepository(NpgsqlDataSource db)
{
    public async Task InsertAsync(string scanType, string status, int? hostsScanned, int? findingsCount, int durationMs)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "INSERT INTO lexi_schema.scan_log (scan_type, status, hosts_scanned, findings_count, duration_ms) VALUES (@scanType, @status, @hostsScanned, @findingsCount, @durationMs)",
            new { scanType, status, hostsScanned, findingsCount, durationMs });
    }
}
