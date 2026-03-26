using Dapper;
using Mediahost.Agents.Data;

namespace Rex.Agent.Data.Repositories;

public class Project
{
    public Guid     Id          { get; init; }
    public string   Name        { get; init; } = "";
    public string?  RepoUrl     { get; init; }
    public string?  LocalPath   { get; init; }
    public string?  Description { get; init; }
    public string?  Language    { get; init; }
    public string   Status      { get; init; } = "active";
    public DateTime CreatedAt   { get; init; }
    public DateTime UpdatedAt   { get; init; }
}

public class ProjectRepository(DbConnectionFactory db)
{
    public async Task<List<Project>> GetAllAsync()
    {
        await using var conn = db.Create();
        var rows = await conn.QueryAsync<Project>(
            "SELECT * FROM rex_schema.projects ORDER BY name");
        return rows.ToList();
    }

    public async Task<Project?> GetByNameAsync(string name)
    {
        await using var conn = db.Create();
        return await conn.QueryFirstOrDefaultAsync<Project>(
            "SELECT * FROM rex_schema.projects WHERE name = @name", new { name });
    }

    public async Task<Guid> UpsertAsync(Project project)
    {
        await using var conn = db.Create();
        return await conn.ExecuteScalarAsync<Guid>("""
            INSERT INTO rex_schema.projects (id, name, repo_url, local_path, description, language, status)
            VALUES (@Id, @Name, @RepoUrl, @LocalPath, @Description, @Language, @Status)
            ON CONFLICT (name) DO UPDATE SET
                repo_url    = EXCLUDED.repo_url,
                local_path  = EXCLUDED.local_path,
                description = EXCLUDED.description,
                language    = EXCLUDED.language,
                status      = EXCLUDED.status,
                updated_at  = NOW()
            RETURNING id
            """, project);
    }

    public async Task DeleteAsync(Guid id)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync("DELETE FROM rex_schema.projects WHERE id = @id", new { id });
    }
}
