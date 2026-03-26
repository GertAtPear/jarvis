using Dapper;
using Mediahost.Llm.Models;

namespace Mediahost.Agents.Data;

/// <summary>
/// Abstract base for all agent memory services.
/// Subclasses override <see cref="Schema"/> to target their own PostgreSQL schema
/// (e.g., "andrew_schema", "eve_schema", "sam_schema").
/// </summary>
public abstract class AgentMemoryService(DbConnectionFactory db) : IAgentMemoryService
{
    private const int MaxMessages = 20;

    /// <summary>PostgreSQL schema that stores this agent's sessions, conversations and memory tables.</summary>
    protected abstract string Schema { get; }

    public async Task EnsureSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync($"""
            INSERT INTO {Schema}.sessions (id)
            VALUES (@sessionId)
            ON CONFLICT (id) DO NOTHING
            """, new { sessionId });
    }

    public async Task<List<LlmMessage>> LoadHistoryAsync(Guid sessionId, CancellationToken ct = default)
    {
        await using var conn = db.Create();
        var rows = await conn.QueryAsync<(string Role, string Content)>($"""
            SELECT role, content
            FROM (
                SELECT role, content, created_at
                FROM {Schema}.conversations
                WHERE session_id = @sessionId
                ORDER BY created_at DESC
                LIMIT @limit
            ) sub
            ORDER BY created_at ASC
            """, new { sessionId, limit = MaxMessages });

        return rows
            .Select(r => new LlmMessage(r.Role, [new TextContent(r.Content)]))
            .ToList();
    }

    public async Task SaveTurnAsync(Guid sessionId, string userMsg, string assistantMsg, CancellationToken ct = default)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync($"""
            INSERT INTO {Schema}.conversations (session_id, role, content)
            VALUES (@sessionId, 'user',      @userMsg),
                   (@sessionId, 'assistant', @assistantMsg)
            """, new { sessionId, userMsg, assistantMsg });

        await conn.ExecuteAsync($"""
            UPDATE {Schema}.sessions
            SET last_message_at = NOW()
            WHERE id = @sessionId
            """, new { sessionId });
    }

    public async Task<Dictionary<string, string>> LoadFactsAsync(CancellationToken ct = default)
    {
        await using var conn = db.Create();
        var rows = await conn.QueryAsync<(string Key, string Value)>(
            $"SELECT key, value FROM {Schema}.memory ORDER BY key");
        return rows.ToDictionary(r => r.Key, r => r.Value);
    }

    public async Task RememberFactAsync(string key, string value, CancellationToken ct = default)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync($"""
            INSERT INTO {Schema}.memory (key, value, updated_at)
            VALUES (@key, @value, NOW())
            ON CONFLICT (key) DO UPDATE SET value = @value, updated_at = NOW()
            """, new { key, value });
    }

    public async Task ForgetFactAsync(string key, CancellationToken ct = default)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync(
            $"DELETE FROM {Schema}.memory WHERE key = @key", new { key });
    }
}
