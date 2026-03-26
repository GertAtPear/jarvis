using Dapper;
using Mediahost.Agents.Data;

namespace Rex.Agent.Data.Repositories;

public class DevSession
{
    public Guid     Id           { get; init; }
    public Guid?    SessionId    { get; init; }
    public string   TaskSummary  { get; init; } = "";
    public string?  Plan         { get; init; }
    public string?  FilesChanged { get; init; }   // JSONB stored as string
    public string   Outcome      { get; init; } = "pending";
    public string?  CommitSha    { get; init; }
    public DateTime CreatedAt    { get; init; }
}

public class DevSessionRepository(DbConnectionFactory db)
{
    public async Task<Guid> CreateAsync(DevSession session)
    {
        await using var conn = db.Create();
        return await conn.ExecuteScalarAsync<Guid>("""
            INSERT INTO rex_schema.dev_sessions
                (id, session_id, task_summary, plan, files_changed, outcome, commit_sha)
            VALUES
                (@Id, @SessionId, @TaskSummary, @Plan,
                 @FilesChanged::jsonb, @Outcome, @CommitSha)
            RETURNING id
            """, session);
    }

    public async Task UpdateOutcomeAsync(Guid id, string outcome, string? commitSha = null)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            UPDATE rex_schema.dev_sessions
            SET outcome = @outcome, commit_sha = @commitSha
            WHERE id = @id
            """, new { id, outcome, commitSha });
    }

    public async Task<List<DevSession>> GetRecentAsync(int limit = 20)
    {
        await using var conn = db.Create();
        var rows = await conn.QueryAsync<DevSession>("""
            SELECT * FROM rex_schema.dev_sessions
            ORDER BY created_at DESC
            LIMIT @limit
            """, new { limit });
        return rows.ToList();
    }
}
