using Dapper;
using Mediahost.Agents.Data;

namespace Research.Agent.Data.Repositories;

public record ResearchDatabase(
    Guid    Id,
    string  Name,
    string? DisplayName,
    string  DbType,
    string  Host,
    int     Port,
    string  DbName,
    string? VaultSecretPath,
    string? Description,
    bool    IsActive);

public class ResearchDatabaseRepository(DbConnectionFactory db)
{
    public async Task<IEnumerable<ResearchDatabase>> GetAllActiveAsync(CancellationToken ct = default)
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<ResearchDatabase>("""
            SELECT id, name, display_name, db_type, host, port, db_name,
                   vault_secret_path, description, is_active
            FROM research_schema.databases
            WHERE is_active = true
            ORDER BY name
            """);
    }

    public async Task<ResearchDatabase?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        await using var conn = db.Create();
        return await conn.QueryFirstOrDefaultAsync<ResearchDatabase>("""
            SELECT id, name, display_name, db_type, host, port, db_name,
                   vault_secret_path, description, is_active
            FROM research_schema.databases
            WHERE name = @name AND is_active = true
            """, new { name });
    }
}
