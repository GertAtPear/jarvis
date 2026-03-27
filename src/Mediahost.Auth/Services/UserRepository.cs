using System.Security.Cryptography;
using System.Text;
using Dapper;
using Mediahost.Auth.Models;
using Mediahost.Agents.Data;
using Microsoft.Extensions.Logging;

namespace Mediahost.Auth.Services;

public class UserRepository(
    DbConnectionFactory db,
    ILogger<UserRepository> logger)
{
    public async Task<UserRecord?> GetByUsernameAsync(string username)
    {
        await using var conn = db.Create();
        var row = await conn.QueryFirstOrDefaultAsync("""
            SELECT u.id, u.username, u.display_name, u.email,
                   u.is_active, u.last_login_at, u.created_at
            FROM jarvis_schema.users u
            WHERE LOWER(u.username) = LOWER(@username) AND u.is_active = true
            """, new { username });

        if (row == null) return null;

        var roles = await GetUserRolesAsync(conn, (Guid)row.id);
        return MapRow(row, roles);
    }

    public async Task<UserRecord?> GetByIdAsync(Guid id)
    {
        await using var conn = db.Create();
        var row = await conn.QueryFirstOrDefaultAsync("""
            SELECT u.id, u.username, u.display_name, u.email,
                   u.is_active, u.last_login_at, u.created_at
            FROM jarvis_schema.users u
            WHERE u.id = @id
            """, new { id });

        if (row == null) return null;

        var roles = await GetUserRolesAsync(conn, id);
        return MapRow(row, roles);
    }

    public async Task<string?> GetPasswordHashAsync(string username)
    {
        await using var conn = db.Create();
        return await conn.ExecuteScalarAsync<string?>("""
            SELECT password_hash FROM jarvis_schema.users
            WHERE LOWER(username) = LOWER(@username) AND is_active = true
            """, new { username });
    }

    public async Task UpdateLastLoginAsync(Guid userId)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            UPDATE jarvis_schema.users SET last_login_at = NOW() WHERE id = @userId
            """, new { userId });
    }

    public async Task<Guid> StoreSessionAsync(
        Guid userId, string tokenHash, DateTimeOffset expiresAt)
    {
        await using var conn = db.Create();
        return await conn.ExecuteScalarAsync<Guid>("""
            INSERT INTO jarvis_schema.user_sessions (user_id, token_hash, expires_at)
            VALUES (@userId, @tokenHash, @expiresAt)
            RETURNING id
            """, new { userId, tokenHash, expiresAt });
    }

    public async Task<UserRecord?> ValidateTokenAsync(string token)
    {
        var hash = HashToken(token);
        await using var conn = db.Create();

        var session = await conn.QueryFirstOrDefaultAsync("""
            SELECT s.user_id, s.expires_at
            FROM jarvis_schema.user_sessions s
            WHERE s.token_hash = @hash AND s.expires_at > NOW()
            """, new { hash });

        if (session == null) return null;

        // Update last_used_at
        await conn.ExecuteAsync("""
            UPDATE jarvis_schema.user_sessions
            SET last_used_at = NOW()
            WHERE token_hash = @hash
            """, new { hash });

        return await GetByIdAsync((Guid)session.user_id);
    }

    public async Task DeleteSessionAsync(string token)
    {
        var hash = HashToken(token);
        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            DELETE FROM jarvis_schema.user_sessions WHERE token_hash = @hash
            """, new { hash });
    }

    public async Task<IReadOnlyList<string>> GetAccessibleAgentsAsync(Guid userId)
    {
        await using var conn = db.Create();

        // Admin role gets all agents
        var isAdmin = await conn.ExecuteScalarAsync<bool>("""
            SELECT EXISTS(
                SELECT 1 FROM jarvis_schema.user_roles ur
                JOIN jarvis_schema.roles r ON r.id = ur.role_id
                WHERE ur.user_id = @userId AND r.role_name = 'admin'
            )
            """, new { userId });

        if (isAdmin) return ["*"];

        var agents = await conn.QueryAsync<string>("""
            SELECT DISTINCT ra.agent_name
            FROM jarvis_schema.role_agent_access ra
            JOIN jarvis_schema.user_roles ur ON ur.role_id = ra.role_id
            WHERE ur.user_id = @userId
            """, new { userId });

        return agents.ToList();
    }

    public async Task<IEnumerable<object>> ListAllUsersAsync()
    {
        await using var conn = db.Create();
        var users = await conn.QueryAsync("""
            SELECT u.id, u.username, u.display_name, u.email,
                   u.is_active, u.last_login_at, u.created_at,
                   array_agg(r.role_name) FILTER (WHERE r.role_name IS NOT NULL) as roles
            FROM jarvis_schema.users u
            LEFT JOIN jarvis_schema.user_roles ur ON ur.user_id = u.id
            LEFT JOIN jarvis_schema.roles r ON r.id = ur.role_id
            GROUP BY u.id, u.username, u.display_name, u.email, u.is_active, u.last_login_at, u.created_at
            ORDER BY u.created_at
            """);
        return users.Select(r => (object)r);
    }

    public async Task<Guid> CreateUserAsync(
        string username, string displayName, string? email, string passwordHash)
    {
        await using var conn = db.Create();
        return await conn.ExecuteScalarAsync<Guid>("""
            INSERT INTO jarvis_schema.users (username, display_name, email, password_hash)
            VALUES (@username, @displayName, @email, @passwordHash)
            RETURNING id
            """, new { username, displayName, email, passwordHash });
    }

    public async Task UpdatePasswordAsync(Guid userId, string passwordHash)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            UPDATE jarvis_schema.users SET password_hash = @passwordHash WHERE id = @userId
            """, new { userId, passwordHash });
    }

    public async Task SetUserRolesAsync(Guid userId, IEnumerable<string> roleNames)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync(
            "DELETE FROM jarvis_schema.user_roles WHERE user_id = @userId",
            new { userId });

        foreach (var roleName in roleNames)
        {
            await conn.ExecuteAsync("""
                INSERT INTO jarvis_schema.user_roles (user_id, role_id)
                SELECT @userId, id FROM jarvis_schema.roles WHERE role_name = @roleName
                ON CONFLICT DO NOTHING
                """, new { userId, roleName });
        }
    }

    public async Task UpdateUserAsync(
        Guid userId, string? displayName, string? email, bool? isActive)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            UPDATE jarvis_schema.users SET
                display_name = COALESCE(@displayName, display_name),
                email        = COALESCE(@email, email),
                is_active    = COALESCE(@isActive, is_active)
            WHERE id = @userId
            """, new { userId, displayName, email, isActive });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<List<string>> GetUserRolesAsync(
        Npgsql.NpgsqlConnection conn, Guid userId)
    {
        var roles = await conn.QueryAsync<string>("""
            SELECT r.role_name FROM jarvis_schema.user_roles ur
            JOIN jarvis_schema.roles r ON r.id = ur.role_id
            WHERE ur.user_id = @userId
            """, new { userId });
        return roles.ToList();
    }

    private static UserRecord MapRow(dynamic row, List<string> roles) => new(
        Id:          (Guid)row.id,
        Username:    (string)row.username,
        DisplayName: (string)row.display_name,
        Email:       (string?)row.email,
        IsActive:    (bool)row.is_active,
        LastLoginAt: row.last_login_at is null ? null : new DateTimeOffset((DateTime)row.last_login_at),
        CreatedAt:   new DateTimeOffset((DateTime)row.created_at),
        Roles:       roles);

    public static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLower();
    }
}
