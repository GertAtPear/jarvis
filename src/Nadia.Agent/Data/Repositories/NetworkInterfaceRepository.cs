using Dapper;
using Nadia.Agent.Models;
using Npgsql;

namespace Nadia.Agent.Data.Repositories;

public class NetworkInterfaceRepository(NpgsqlDataSource db)
{
    public async Task<IEnumerable<NetworkInterfaceRecord>> GetAllAsync()
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QueryAsync<NetworkInterfaceRecord>(
            "SELECT id, name, display_name, if_type, ip_address, subnet, is_active, vault_secret_path, created_at FROM nadia_schema.network_interfaces ORDER BY display_name");
    }

    public async Task<NetworkInterfaceRecord?> GetByNameAsync(string name)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QueryFirstOrDefaultAsync<NetworkInterfaceRecord>(
            "SELECT id, name, display_name, if_type, ip_address, subnet, is_active, vault_secret_path, created_at FROM nadia_schema.network_interfaces WHERE name = @name",
            new { name });
    }

    public async Task UpsertAsync(string name, string displayName, string ifType, string? ipAddress, string? subnet)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO nadia_schema.network_interfaces (name, display_name, if_type, ip_address, subnet)
            VALUES (@name, @displayName, @ifType, @ipAddress, @subnet)
            ON CONFLICT (name) DO UPDATE SET
                display_name = EXCLUDED.display_name,
                if_type      = EXCLUDED.if_type,
                ip_address   = EXCLUDED.ip_address,
                subnet       = EXCLUDED.subnet,
                updated_at   = NOW()
            """,
            new { name, displayName, ifType, ipAddress, subnet });
    }
}
