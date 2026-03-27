using Dapper;
using Lexi.Agent.Models;
using Npgsql;

namespace Lexi.Agent.Data.Repositories;

public class SoftwareRepository(NpgsqlDataSource db)
{
    public async Task<IEnumerable<SoftwareInventoryRecord>> GetByHostAsync(string? host = null, string? packageManager = null)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QueryAsync<SoftwareInventoryRecord>(
            """
            SELECT id, host, package_name, version, package_manager, scanned_at
            FROM lexi_schema.software_inventory
            WHERE (@host IS NULL OR host = @host)
              AND (@packageManager IS NULL OR package_manager = @packageManager)
            ORDER BY host, package_name
            """,
            new { host, packageManager });
    }

    public async Task BulkUpsertAsync(string host, IEnumerable<(string Name, string Version, string Manager)> packages)
    {
        await using var conn = await db.OpenConnectionAsync();
        foreach (var p in packages)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO lexi_schema.software_inventory (host, package_name, version, package_manager)
                VALUES (@host, @name, @version, @manager)
                ON CONFLICT (host, package_name, package_manager) DO UPDATE SET
                    version    = EXCLUDED.version,
                    scanned_at = NOW()
                """,
                new { host, name = p.Name, version = p.Version, manager = p.Manager });
        }
    }
}
