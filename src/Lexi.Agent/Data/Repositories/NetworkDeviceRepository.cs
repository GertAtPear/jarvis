using Dapper;
using Lexi.Agent.Models;
using Npgsql;

namespace Lexi.Agent.Data.Repositories;

public class NetworkDeviceRepository(NpgsqlDataSource db)
{
    public async Task<IEnumerable<NetworkDeviceRecord>> GetAllAsync()
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QueryAsync<NetworkDeviceRecord>(
            """
            SELECT id, mac_address, ip_address, hostname, vendor, first_seen, last_seen,
                   is_known, device_name, notes, scanned_at
            FROM lexi_schema.network_devices
            ORDER BY is_known ASC, last_seen DESC
            """);
    }

    public async Task<IEnumerable<NetworkDeviceRecord>> GetUnknownAsync()
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QueryAsync<NetworkDeviceRecord>(
            "SELECT id, mac_address, ip_address, hostname, vendor, first_seen, last_seen, is_known, device_name, notes, scanned_at FROM lexi_schema.network_devices WHERE is_known = false ORDER BY last_seen DESC");
    }

    public async Task BulkUpsertAsync(IEnumerable<(string Mac, string? Ip, string? Hostname, string? Vendor)> devices)
    {
        await using var conn = await db.OpenConnectionAsync();
        foreach (var d in devices)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO lexi_schema.network_devices (mac_address, ip_address, hostname, vendor)
                VALUES (@mac, @ip, @hostname, @vendor)
                ON CONFLICT (mac_address) DO UPDATE SET
                    ip_address = EXCLUDED.ip_address,
                    hostname   = EXCLUDED.hostname,
                    vendor     = COALESCE(EXCLUDED.vendor, lexi_schema.network_devices.vendor),
                    last_seen  = NOW(),
                    scanned_at = NOW()
                """,
                new { mac = d.Mac, ip = d.Ip, hostname = d.Hostname, vendor = d.Vendor });
        }
    }

    public async Task MarkKnownAsync(Guid id, string deviceName, string? notes = null)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE lexi_schema.network_devices SET is_known = true, device_name = @deviceName, notes = COALESCE(@notes, notes) WHERE id = @id",
            new { id, deviceName, notes });
    }
}
