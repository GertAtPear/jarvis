using Dapper;
using Nadia.Agent.Models;
using Npgsql;

namespace Nadia.Agent.Data.Repositories;

public class WifiNodeRepository(NpgsqlDataSource db)
{
    public async Task<IEnumerable<WifiNodeRecord>> GetAllAsync()
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QueryAsync<WifiNodeRecord>(
            "SELECT id, ssid, bssid, channel, signal_dbm, connected_clients, ap_host, scanned_at FROM nadia_schema.wifi_nodes ORDER BY ssid");
    }

    public async Task BulkUpsertAsync(IEnumerable<(string Ssid, string Bssid, int? Channel, int? SignalDbm, int? ConnectedClients, string? ApHost)> nodes)
    {
        await using var conn = await db.OpenConnectionAsync();
        foreach (var n in nodes)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO nadia_schema.wifi_nodes (ssid, bssid, channel, signal_dbm, connected_clients, ap_host)
                VALUES (@ssid, @bssid, @channel, @signal, @clients, @apHost)
                ON CONFLICT (bssid) DO UPDATE SET
                    ssid              = EXCLUDED.ssid,
                    channel           = EXCLUDED.channel,
                    signal_dbm        = EXCLUDED.signal_dbm,
                    connected_clients = EXCLUDED.connected_clients,
                    scanned_at        = NOW()
                """,
                new { ssid = n.Ssid, bssid = n.Bssid, channel = n.Channel, signal = n.SignalDbm, clients = n.ConnectedClients, apHost = n.ApHost });
        }
    }
}
