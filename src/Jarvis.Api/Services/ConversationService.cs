using Dapper;
using Jarvis.Api.Data;
using Jarvis.Api.Models;

namespace Jarvis.Api.Services;

public class ConversationService(DbConnectionFactory db)
{
    private const int TitleMaxLength = 80;

    public async Task<Guid> StartSessionAsync(string? title = null)
    {
        await using var conn = db.Create();
        const string sql = """
            INSERT INTO jarvis_schema.sessions (title)
            VALUES (@title)
            RETURNING id
            """;
        return await conn.ExecuteScalarAsync<Guid>(sql, new { title });
    }

    public async Task SaveMessageAsync(Guid sessionId, AgentMessage message)
    {
        await using var conn = db.Create();
        const string sql = """
            INSERT INTO jarvis_schema.conversations
                (session_id, role, agent_name, content, tool_calls, department)
            VALUES
                (@sessionId, @Role, @AgentName, @Content, @ToolCallsJson::jsonb, @Department)
            """;
        await conn.ExecuteAsync(sql, new
        {
            sessionId,
            message.Role,
            message.AgentName,
            message.Content,
            ToolCallsJson = message.ToolCallsJson ?? "null",
            message.Department
        });

        // Keep session's last_message_at and message_count in sync
        await conn.ExecuteAsync("""
            UPDATE jarvis_schema.sessions
            SET last_message_at = NOW(),
                message_count   = message_count + 1
            WHERE id = @sessionId
            """, new { sessionId });
    }

    public async Task<IEnumerable<AgentMessage>> GetHistoryAsync(Guid sessionId, int limit = 50)
    {
        await using var conn = db.Create();
        const string sql = """
            SELECT id, session_id AS SessionId, role, agent_name AS AgentName,
                   content, tool_calls::text AS ToolCallsJson, department, created_at AS CreatedAt
            FROM jarvis_schema.conversations
            WHERE session_id = @sessionId
            ORDER BY created_at DESC
            LIMIT @limit
            """;
        var rows = await conn.QueryAsync<AgentMessage>(sql, new { sessionId, limit });
        // Return in chronological order (oldest first)
        return rows.Reverse();
    }

    public async Task<IEnumerable<SessionSummary>> GetRecentSessionsAsync(int count = 30)
    {
        await using var conn = db.Create();
        const string sql = """
            SELECT id, title, department, message_count AS MessageCount,
                   last_message_at AS LastMessageAt, created_at AS CreatedAt
            FROM jarvis_schema.sessions
            ORDER BY last_message_at DESC
            LIMIT @count
            """;
        return await conn.QueryAsync<SessionSummary>(sql, new { count });
    }

    public async Task UpdateSessionTitleAsync(Guid sessionId, string title)
    {
        var truncated = title.Length > TitleMaxLength
            ? title[..TitleMaxLength]
            : title;

        await using var conn = db.Create();
        await conn.ExecuteAsync(
            "UPDATE jarvis_schema.sessions SET title = @title WHERE id = @sessionId",
            new { sessionId, title = truncated });
    }

    /// <summary>
    /// Returns true if the session has no existing messages (i.e., this is the first turn).
    /// Used to trigger the session title update after the first response.
    /// </summary>
    /// <summary>
    /// Deletes the most recent 2 messages (user + assistant exchange) from the session.
    /// Used after a vault write to remove the secret from conversation history.
    /// </summary>
    public async Task PurgeLastExchangeAsync(Guid sessionId)
    {
        await using var conn = db.Create();
        const string sql = """
            DELETE FROM jarvis_schema.conversations
            WHERE id IN (
                SELECT id FROM jarvis_schema.conversations
                WHERE session_id = @sessionId
                ORDER BY created_at DESC
                LIMIT 2
            )
            """;
        var deleted = await conn.ExecuteAsync(sql, new { sessionId });
        if (deleted > 0)
        {
            await conn.ExecuteAsync("""
                UPDATE jarvis_schema.sessions
                SET message_count = GREATEST(0, message_count - @deleted)
                WHERE id = @sessionId
                """, new { sessionId, deleted });
        }
    }

    /// <summary>
    /// Returns true if the session has no existing messages (i.e., this is the first turn).
    /// Used to trigger the session title update after the first response.
    /// </summary>
    public async Task<bool> IsFirstMessageAsync(Guid sessionId)
    {
        await using var conn = db.Create();
        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT message_count FROM jarvis_schema.sessions WHERE id = @sessionId",
            new { sessionId });
        return count == 0;
    }
}
