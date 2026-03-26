using Mediahost.Agents.Data;
using Andrew.Agent.Models;
using Dapper;

namespace Andrew.Agent.Data.Repositories;

public class ServerRepository(DbConnectionFactory db)
{
    private const string SelectColumns = """
        id,
        hostname,
        host(ip_address) AS IpAddress,
        ssh_port       AS SshPort,
        os_name        AS OsName,
        os_version     AS OsVersion,
        cpu_cores      AS CpuCores,
        ram_gb         AS RamGb,
        disk_total_gb  AS DiskTotalGb,
        disk_used_gb   AS DiskUsedGb,
        status,
        vault_secret_path  AS VaultSecretPath,
        last_scanned_at    AS LastScannedAt,
        last_seen_at       AS LastSeenAt,
        notes,
        tags,
        created_at     AS CreatedAt,
        updated_at     AS UpdatedAt
        """;

    public async Task<IEnumerable<ServerInfo>> GetAllAsync()
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<ServerInfo>(
            $"SELECT {SelectColumns} FROM andrew_schema.servers ORDER BY hostname");
    }

    public async Task<ServerInfo?> GetByHostnameAsync(string hostname)
    {
        await using var conn = db.Create();
        return await conn.QuerySingleOrDefaultAsync<ServerInfo>(
            $"SELECT {SelectColumns} FROM andrew_schema.servers WHERE hostname = @hostname",
            new { hostname });
    }

    public async Task<ServerInfo?> GetByIdAsync(Guid id)
    {
        await using var conn = db.Create();
        return await conn.QuerySingleOrDefaultAsync<ServerInfo>(
            $"SELECT {SelectColumns} FROM andrew_schema.servers WHERE id = @id",
            new { id });
    }

    public async Task<Guid> UpsertAsync(ServerInfo server)
    {
        await using var conn = db.Create();
        const string sql = """
            INSERT INTO andrew_schema.servers
                (hostname, ip_address, ssh_port, os_name, os_version, cpu_cores,
                 ram_gb, disk_total_gb, disk_used_gb, status, vault_secret_path,
                 last_seen_at, notes, tags)
            VALUES
                (@Hostname, CAST(@IpAddress AS inet), @SshPort, @OsName, @OsVersion, @CpuCores,
                 @RamGb, @DiskTotalGb, @DiskUsedGb, @Status, @VaultSecretPath,
                 NOW(), @Notes, CAST(@Tags AS jsonb))
            ON CONFLICT (hostname) DO UPDATE SET
                ip_address        = EXCLUDED.ip_address,
                ssh_port          = EXCLUDED.ssh_port,
                os_name           = EXCLUDED.os_name,
                os_version        = EXCLUDED.os_version,
                cpu_cores         = EXCLUDED.cpu_cores,
                ram_gb            = EXCLUDED.ram_gb,
                disk_total_gb     = EXCLUDED.disk_total_gb,
                disk_used_gb      = EXCLUDED.disk_used_gb,
                status            = EXCLUDED.status,
                vault_secret_path = EXCLUDED.vault_secret_path,
                last_seen_at      = NOW(),
                notes             = EXCLUDED.notes,
                tags              = EXCLUDED.tags,
                updated_at        = NOW()
            RETURNING id
            """;
        return await conn.ExecuteScalarAsync<Guid>(sql, server);
    }

    public async Task UpdateStatusAsync(Guid id, string status)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync(
            "UPDATE andrew_schema.servers SET status = @status, updated_at = NOW() WHERE id = @id",
            new { id, status });
    }

    public async Task UpdateLastScannedAsync(Guid id)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync(
            "UPDATE andrew_schema.servers SET last_scanned_at = NOW(), updated_at = NOW() WHERE id = @id",
            new { id });
    }

    public async Task<IEnumerable<ServerInfo>> GetByStatusAsync(string status)
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<ServerInfo>(
            $"SELECT {SelectColumns} FROM andrew_schema.servers WHERE status = @status ORDER BY hostname",
            new { status });
    }
}
