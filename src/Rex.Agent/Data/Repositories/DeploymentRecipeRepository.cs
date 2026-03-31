using Dapper;
using Mediahost.Agents.Data;

namespace Rex.Agent.Data.Repositories;

public class DeploymentRecipeRepository(DbConnectionFactory db)
{
    public async Task<dynamic?> GetByAppNameAsync(string appName)
    {
        await using var conn = db.Create();
        return await conn.QuerySingleOrDefaultAsync("""
            SELECT
                id,
                app_name      AS AppName,
                description,
                target_server AS TargetServer,
                steps::text         AS StepsJson,
                pre_checks::text    AS PreChecksJson,
                post_checks::text   AS PostChecksJson,
                created_by    AS CreatedBy,
                created_at    AS CreatedAt,
                updated_at    AS UpdatedAt
            FROM rex_schema.deployment_recipes
            WHERE app_name = @appName
            """, new { appName });
    }

    public async Task<IEnumerable<dynamic>> GetAllAsync()
    {
        await using var conn = db.Create();
        return await conn.QueryAsync("""
            SELECT
                app_name      AS AppName,
                description,
                target_server AS TargetServer,
                created_by    AS CreatedBy,
                created_at    AS CreatedAt,
                updated_at    AS UpdatedAt
            FROM rex_schema.deployment_recipes
            ORDER BY app_name
            """);
    }

    public async Task UpsertAsync(
        string appName,
        string? description,
        string targetServer,
        string stepsJson,
        string? preChecksJson,
        string? postChecksJson,
        string createdBy = "rex")
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            INSERT INTO rex_schema.deployment_recipes
                (app_name, description, target_server, steps, pre_checks, post_checks, created_by, updated_at)
            VALUES
                (@appName, @description, @targetServer, @stepsJson::jsonb,
                 @preChecksJson::jsonb, @postChecksJson::jsonb, @createdBy, NOW())
            ON CONFLICT (app_name) DO UPDATE SET
                description   = EXCLUDED.description,
                target_server = EXCLUDED.target_server,
                steps         = EXCLUDED.steps,
                pre_checks    = EXCLUDED.pre_checks,
                post_checks   = EXCLUDED.post_checks,
                updated_at    = NOW()
            """, new
        {
            appName,
            description,
            targetServer,
            stepsJson,
            preChecksJson  = preChecksJson  ?? "[]",
            postChecksJson = postChecksJson ?? "[]",
            createdBy
        });
    }
}
