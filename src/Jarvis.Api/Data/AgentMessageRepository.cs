using Dapper;
using Jarvis.Api.Models;

namespace Jarvis.Api.Data;

public class AgentMessageRepository(DbConnectionFactory db)
{
    public async Task<List<AgentActivityMessage>> GetRecentAsync(int limit = 100, CancellationToken ct = default)
    {
        await using var conn = db.Create();
        var rows = await conn.QueryAsync<AgentActivityMessage>("""
            SELECT
                id,
                from_agent      AS FromAgent,
                to_agent        AS ToAgent,
                message         AS Message,
                thread_id       AS ThreadId,
                requires_approval AS RequiresApproval,
                approved_at     AS ApprovedAt,
                denied_at       AS DeniedAt,
                read_at         AS ReadAt,
                created_at      AS CreatedAt
            FROM jarvis_schema.agent_messages
            ORDER BY created_at DESC
            LIMIT @limit
            """, new { limit });
        return rows.Reverse().ToList();
    }

    public async Task<List<AgentActivityMessage>> GetPendingApprovalAsync(CancellationToken ct = default)
    {
        await using var conn = db.Create();
        var rows = await conn.QueryAsync<AgentActivityMessage>("""
            SELECT
                id,
                from_agent      AS FromAgent,
                to_agent        AS ToAgent,
                message         AS Message,
                thread_id       AS ThreadId,
                requires_approval AS RequiresApproval,
                approved_at     AS ApprovedAt,
                denied_at       AS DeniedAt,
                read_at         AS ReadAt,
                created_at      AS CreatedAt
            FROM jarvis_schema.agent_messages
            WHERE requires_approval = TRUE
              AND approved_at IS NULL
              AND denied_at IS NULL
            ORDER BY created_at ASC
            """);
        return rows.ToList();
    }

    public async Task<List<AgentActivityMessage>> GetSinceAsync(long sinceId, CancellationToken ct = default)
    {
        await using var conn = db.Create();
        var rows = await conn.QueryAsync<AgentActivityMessage>("""
            SELECT
                id,
                from_agent      AS FromAgent,
                to_agent        AS ToAgent,
                message         AS Message,
                thread_id       AS ThreadId,
                requires_approval AS RequiresApproval,
                approved_at     AS ApprovedAt,
                denied_at       AS DeniedAt,
                read_at         AS ReadAt,
                created_at      AS CreatedAt
            FROM jarvis_schema.agent_messages
            WHERE id > @sinceId
            ORDER BY id ASC
            LIMIT 50
            """, new { sinceId });
        return rows.ToList();
    }

    public async Task<bool> ApproveAsync(long id, CancellationToken ct = default)
    {
        await using var conn = db.Create();
        var affected = await conn.ExecuteAsync("""
            UPDATE jarvis_schema.agent_messages
            SET approved_at = NOW()
            WHERE id = @id AND requires_approval = TRUE AND approved_at IS NULL AND denied_at IS NULL
            """, new { id });
        return affected > 0;
    }

    public async Task<bool> DenyAsync(long id, CancellationToken ct = default)
    {
        await using var conn = db.Create();
        var affected = await conn.ExecuteAsync("""
            UPDATE jarvis_schema.agent_messages
            SET denied_at = NOW()
            WHERE id = @id AND requires_approval = TRUE AND approved_at IS NULL AND denied_at IS NULL
            """, new { id });
        return affected > 0;
    }

    public async Task<long> GetMaxIdAsync(CancellationToken ct = default)
    {
        await using var conn = db.Create();
        return await conn.QuerySingleOrDefaultAsync<long>(
            "SELECT COALESCE(MAX(id), 0) FROM jarvis_schema.agent_messages");
    }
}
