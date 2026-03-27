using Dapper;
using Jarvis.Api.Models;

namespace Jarvis.Api.Data;

public class DeviceRepository(DbConnectionFactory db)
{
    // ── Devices ───────────────────────────────────────────────────────────────

    public async Task<IEnumerable<DeviceRecord>> GetAllAsync()
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<DeviceRecord>("""
            SELECT
                id,
                name,
                device_type             AS DeviceType,
                os_platform             AS OsPlatform,
                registration_token      AS RegistrationToken,
                token_expires_at        AS TokenExpiresAt,
                device_token_path       AS DeviceTokenPath,
                status,
                last_seen_at            AS LastSeenAt,
                hostname,
                ip_address::text        AS IpAddress,
                lah_version             AS LahVersion,
                advertised_modules::text AS AdvertisedModulesJson,
                created_at              AS CreatedAt
            FROM jarvis_schema.devices
            ORDER BY created_at DESC
            """);
    }

    public async Task<DeviceRecord?> GetByIdAsync(Guid id)
    {
        await using var conn = db.Create();
        return await conn.QuerySingleOrDefaultAsync<DeviceRecord>("""
            SELECT
                id,
                name,
                device_type             AS DeviceType,
                os_platform             AS OsPlatform,
                registration_token      AS RegistrationToken,
                token_expires_at        AS TokenExpiresAt,
                device_token_path       AS DeviceTokenPath,
                status,
                last_seen_at            AS LastSeenAt,
                hostname,
                ip_address::text        AS IpAddress,
                lah_version             AS LahVersion,
                advertised_modules::text AS AdvertisedModulesJson,
                created_at              AS CreatedAt
            FROM jarvis_schema.devices
            WHERE id = @id
            """, new { id });
    }

    public async Task<DeviceRecord?> GetByTokenAsync(string registrationToken)
    {
        await using var conn = db.Create();
        return await conn.QuerySingleOrDefaultAsync<DeviceRecord>("""
            SELECT
                id, name, device_type AS DeviceType, os_platform AS OsPlatform,
                registration_token AS RegistrationToken, token_expires_at AS TokenExpiresAt,
                device_token_path AS DeviceTokenPath, status, last_seen_at AS LastSeenAt,
                hostname, ip_address::text AS IpAddress, lah_version AS LahVersion,
                advertised_modules::text AS AdvertisedModulesJson, created_at AS CreatedAt
            FROM jarvis_schema.devices
            WHERE registration_token = @registrationToken
            """, new { registrationToken });
    }

    public async Task<Guid> CreateAsync(string name, string? osPlatform)
    {
        await using var conn = db.Create();
        return await conn.ExecuteScalarAsync<Guid>("""
            INSERT INTO jarvis_schema.devices (name, os_platform)
            VALUES (@name, @osPlatform)
            RETURNING id
            """, new { name, osPlatform });
    }

    public async Task SetRegistrationTokenAsync(Guid id, string token, DateTimeOffset expiresAt)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            UPDATE jarvis_schema.devices
            SET registration_token = @token, token_expires_at = @expiresAt
            WHERE id = @id
            """, new { id, token, expiresAt });
    }

    public async Task ConsumeRegistrationTokenAsync(Guid id, string deviceTokenPath)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            UPDATE jarvis_schema.devices
            SET registration_token = NULL,
                token_expires_at   = NULL,
                device_token_path  = @deviceTokenPath,
                status             = 'offline'
            WHERE id = @id
            """, new { id, deviceTokenPath });
    }

    public async Task UpdateStatusAsync(
        Guid id, string status, DateTimeOffset? lastSeenAt,
        string? hostname, string? ipAddress, string? lahVersion, string? advertisedModulesJson)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            UPDATE jarvis_schema.devices
            SET status             = @status,
                last_seen_at       = @lastSeenAt,
                hostname           = COALESCE(@hostname, hostname),
                ip_address         = COALESCE(@ipAddress::inet, ip_address),
                lah_version        = COALESCE(@lahVersion, lah_version),
                advertised_modules = COALESCE(@advertisedModulesJson::jsonb, advertised_modules)
            WHERE id = @id
            """, new { id, status, lastSeenAt, hostname, ipAddress, lahVersion, advertisedModulesJson });
    }

    public async Task DeleteAsync(Guid id)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync("DELETE FROM jarvis_schema.devices WHERE id = @id", new { id });
    }

    // ── Permissions ───────────────────────────────────────────────────────────

    public async Task<IEnumerable<DevicePermissionRecord>> GetPermissionsAsync(Guid deviceId)
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<DevicePermissionRecord>("""
            SELECT
                id, device_id AS DeviceId, agent_name AS AgentName,
                capability, is_granted AS IsGranted, require_confirm AS RequireConfirm,
                path_scope AS PathScope, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM jarvis_schema.device_permissions
            WHERE device_id = @deviceId
            ORDER BY agent_name, capability
            """, new { deviceId });
    }

    public async Task UpsertPermissionAsync(Guid deviceId, string agentName, string capability,
        bool isGranted, bool requireConfirm, string[]? pathScope)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            INSERT INTO jarvis_schema.device_permissions
                (device_id, agent_name, capability, is_granted, require_confirm, path_scope)
            VALUES
                (@deviceId, @agentName, @capability, @isGranted, @requireConfirm, @pathScope)
            ON CONFLICT (device_id, agent_name, capability) DO UPDATE SET
                is_granted      = EXCLUDED.is_granted,
                require_confirm = EXCLUDED.require_confirm,
                path_scope      = EXCLUDED.path_scope,
                updated_at      = NOW()
            """, new { deviceId, agentName, capability, isGranted, requireConfirm, pathScope });
    }

    public async Task<DevicePermissionRecord?> GetPermissionAsync(
        Guid deviceId, string agentName, string capability)
    {
        await using var conn = db.Create();
        return await conn.QuerySingleOrDefaultAsync<DevicePermissionRecord>("""
            SELECT
                id, device_id AS DeviceId, agent_name AS AgentName,
                capability, is_granted AS IsGranted, require_confirm AS RequireConfirm,
                path_scope AS PathScope, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM jarvis_schema.device_permissions
            WHERE device_id = @deviceId AND agent_name = @agentName AND capability = @capability
            """, new { deviceId, agentName, capability });
    }

    // ── Tool log ──────────────────────────────────────────────────────────────

    public async Task<IEnumerable<DeviceToolLogEntry>> GetToolLogAsync(Guid deviceId, int limit = 50)
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<DeviceToolLogEntry>("""
            SELECT
                id, device_id AS DeviceId, agent_name AS AgentName,
                tool_name AS ToolName, parameters::text AS ParametersJson,
                success, error_message AS ErrorMessage, duration_ms AS DurationMs,
                confirmed_by_user AS ConfirmedByUser, created_at AS CreatedAt
            FROM jarvis_schema.device_tool_log
            WHERE device_id = @deviceId
            ORDER BY created_at DESC
            LIMIT @limit
            """, new { deviceId, limit });
    }

    public async Task LogToolCallAsync(
        Guid deviceId, string agentName, string toolName,
        string? parametersJson, bool success, string? errorMessage,
        int durationMs, bool confirmedByUser)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            INSERT INTO jarvis_schema.device_tool_log
                (device_id, agent_name, tool_name, parameters, success,
                 error_message, duration_ms, confirmed_by_user)
            VALUES
                (@deviceId, @agentName, @toolName, @parametersJson::jsonb, @success,
                 @errorMessage, @durationMs, @confirmedByUser)
            """, new { deviceId, agentName, toolName, parametersJson, success,
                       errorMessage, durationMs, confirmedByUser });
    }
}
