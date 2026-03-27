using Dapper;
using Npgsql;
using Sam.Agent.Models;

namespace Sam.Agent.Data.Repositories;

public class DatabaseRepository(NpgsqlDataSource db)
{
    public async Task<IEnumerable<DatabaseRecord>> GetAllAsync()
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QueryAsync<DatabaseRecord>(
            "SELECT id, name, display_name, db_type, host, port, db_name, vault_secret_path, status, last_scanned_at, notes, created_at FROM sam_schema.databases ORDER BY display_name");
    }

    public async Task<DatabaseRecord?> GetByNameAsync(string name)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QueryFirstOrDefaultAsync<DatabaseRecord>(
            "SELECT id, name, display_name, db_type, host, port, db_name, vault_secret_path, status, last_scanned_at, notes, created_at FROM sam_schema.databases WHERE name = @name",
            new { name });
    }

    public async Task<DatabaseRecord?> GetByIdAsync(Guid id)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QueryFirstOrDefaultAsync<DatabaseRecord>(
            "SELECT id, name, display_name, db_type, host, port, db_name, vault_secret_path, status, last_scanned_at, notes, created_at FROM sam_schema.databases WHERE id = @id",
            new { id });
    }

    public async Task UpdateStatusAsync(Guid id, string status)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE sam_schema.databases SET status = @status, last_scanned_at = NOW(), updated_at = NOW() WHERE id = @id",
            new { id, status });
    }
}
