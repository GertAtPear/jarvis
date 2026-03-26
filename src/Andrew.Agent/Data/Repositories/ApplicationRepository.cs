using Mediahost.Agents.Data;
using Andrew.Agent.Models;
using Dapper;

namespace Andrew.Agent.Data.Repositories;

public class ApplicationRepository(DbConnectionFactory db)
{
    private const string SelectColumns = """
        id,
        name,
        server_id           AS ServerId,
        container_id        AS ContainerId,
        app_type            AS AppType,
        framework,
        port,
        config_path         AS ConfigPath,
        git_repo_url        AS GitRepoUrl,
        health_check_url    AS HealthCheckUrl,
        notes,
        last_seen_running_at AS LastSeenRunningAt,
        created_at          AS CreatedAt,
        updated_at          AS UpdatedAt
        """;

    public async Task<IEnumerable<ApplicationInfo>> GetAllAsync()
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<ApplicationInfo>(
            $"SELECT {SelectColumns} FROM andrew_schema.applications ORDER BY name");
    }

    public async Task<IEnumerable<ApplicationInfo>> GetByServerIdAsync(Guid serverId)
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<ApplicationInfo>(
            $"SELECT {SelectColumns} FROM andrew_schema.applications WHERE server_id = @serverId ORDER BY name",
            new { serverId });
    }

    public async Task<IEnumerable<ApplicationInfo>> SearchByNameAsync(string partialName)
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<ApplicationInfo>(
            $"SELECT {SelectColumns} FROM andrew_schema.applications WHERE name ILIKE @pattern ORDER BY name",
            new { pattern = $"%{partialName}%" });
    }

    public async Task UpsertAsync(ApplicationInfo app)
    {
        await using var conn = db.Create();
        const string sql = """
            INSERT INTO andrew_schema.applications
                (name, server_id, container_id, app_type, framework, port,
                 config_path, git_repo_url, health_check_url, notes, last_seen_running_at)
            VALUES
                (@Name, @ServerId, @ContainerId, @AppType, @Framework, @Port,
                 @ConfigPath, @GitRepoUrl, @HealthCheckUrl, @Notes, @LastSeenRunningAt)
            ON CONFLICT (id) DO UPDATE SET
                name                 = EXCLUDED.name,
                server_id            = EXCLUDED.server_id,
                container_id         = EXCLUDED.container_id,
                app_type             = EXCLUDED.app_type,
                framework            = EXCLUDED.framework,
                port                 = EXCLUDED.port,
                config_path          = EXCLUDED.config_path,
                git_repo_url         = EXCLUDED.git_repo_url,
                health_check_url     = EXCLUDED.health_check_url,
                notes                = EXCLUDED.notes,
                last_seen_running_at = EXCLUDED.last_seen_running_at,
                updated_at           = NOW()
            """;
        await conn.ExecuteAsync(sql, app);
    }
}
