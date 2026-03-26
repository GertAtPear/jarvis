using Dapper;
using Rocky.Agent.Data;
using Rocky.Agent.Models;

namespace Rocky.Agent.Data.Repositories;

public class WatchedServiceRepository(DbConnectionFactory db)
{
    private const string SelectColumns = """
        id,
        name,
        display_name    AS DisplayName,
        check_type      AS CheckType,
        check_config    AS CheckConfig,
        interval_seconds AS IntervalSeconds,
        enabled,
        vault_secret_path AS VaultSecretPath,
        server_id       AS ServerId,
        created_at      AS CreatedAt,
        updated_at      AS UpdatedAt
        """;

    public async Task<IEnumerable<WatchedService>> GetAllEnabledAsync()
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<WatchedService>(
            $"SELECT {SelectColumns} FROM rocky_schema.watched_services WHERE enabled = true ORDER BY name");
    }

    public async Task<WatchedService?> GetByIdAsync(Guid id)
    {
        await using var conn = db.Create();
        return await conn.QuerySingleOrDefaultAsync<WatchedService>(
            $"SELECT {SelectColumns} FROM rocky_schema.watched_services WHERE id = @id",
            new { id });
    }

    public async Task<WatchedService?> GetByNameAsync(string name)
    {
        await using var conn = db.Create();
        return await conn.QuerySingleOrDefaultAsync<WatchedService>(
            $"SELECT {SelectColumns} FROM rocky_schema.watched_services WHERE name = @name",
            new { name });
    }

    public async Task<IEnumerable<WatchedService>> GetAllAsync()
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<WatchedService>(
            $"SELECT {SelectColumns} FROM rocky_schema.watched_services ORDER BY name");
    }
}
