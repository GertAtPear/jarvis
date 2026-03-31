using Dapper;

namespace Mediahost.Agents.Data;

/// <summary>
/// Read-only access to the shared platform context (platform_schema.user_context).
/// Registered as a singleton by AddAgentInfrastructure — available to every agent.
///
/// Only Eve has write access via the share_context / unshare_context tools.
/// All other agents consume this read-only in their system prompt.
/// </summary>
public class SharedMemoryService(DbConnectionFactory db)
{
    public async Task<Dictionary<string, string>> LoadSharedContextAsync(CancellationToken ct = default)
    {
        await using var conn = db.Create();
        var rows = await conn.QueryAsync<(string Key, string Value)>(
            "SELECT key, value FROM platform_schema.user_context ORDER BY key");
        return rows.ToDictionary(r => r.Key, r => r.Value);
    }

    public async Task WriteContextAsync(string key, string value, string authorAgent, CancellationToken ct = default)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            INSERT INTO platform_schema.user_context (key, value, author_agent, updated_at)
            VALUES (@key, @value, @author, NOW())
            ON CONFLICT (key) DO UPDATE SET value = @value, author_agent = @author, updated_at = NOW()
            """, new { key, value, author = authorAgent });
    }

    public async Task DeleteContextAsync(string key, CancellationToken ct = default)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync(
            "DELETE FROM platform_schema.user_context WHERE key = @key", new { key });
    }
}
