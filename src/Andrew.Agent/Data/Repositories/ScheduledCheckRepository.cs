using Mediahost.Agents.Data;
using System.Text.Json;
using Andrew.Agent.Models;
using Dapper;

namespace Andrew.Agent.Data.Repositories;

public class ScheduledCheckRepository(DbConnectionFactory db)
{
    private const string SelectColumns = """
        id,
        name,
        check_type          AS CheckType,
        target,
        server_id           AS ServerId,
        schedule_type       AS ScheduleType,
        interval_minutes    AS IntervalMinutes,
        cron_expression     AS CronExpression,
        is_active           AS IsActive,
        notify_on_failure   AS NotifyOnFailure,
        last_checked_at     AS LastCheckedAt,
        last_status         AS LastStatus,
        last_result::text   AS LastResultJson,
        created_at          AS CreatedAt,
        updated_at          AS UpdatedAt
        """;

    public async Task<IEnumerable<ScheduledCheck>> GetAllActiveAsync()
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<ScheduledCheck>(
            $"SELECT {SelectColumns} FROM andrew_schema.scheduled_checks WHERE is_active = true ORDER BY name");
    }

    public async Task<IEnumerable<ScheduledCheck>> GetAllAsync()
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<ScheduledCheck>(
            $"SELECT {SelectColumns} FROM andrew_schema.scheduled_checks ORDER BY name");
    }

    public async Task<ScheduledCheck?> GetByIdAsync(Guid id)
    {
        await using var conn = db.Create();
        return await conn.QuerySingleOrDefaultAsync<ScheduledCheck>(
            $"SELECT {SelectColumns} FROM andrew_schema.scheduled_checks WHERE id = @id",
            new { id });
    }

    public async Task<ScheduledCheck?> GetByNameAsync(string name)
    {
        await using var conn = db.Create();
        return await conn.QuerySingleOrDefaultAsync<ScheduledCheck>(
            $"SELECT {SelectColumns} FROM andrew_schema.scheduled_checks WHERE LOWER(name) = LOWER(@name)",
            new { name });
    }

    public async Task<Guid> CreateAsync(ScheduledCheck check)
    {
        await using var conn = db.Create();
        return await conn.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO andrew_schema.scheduled_checks
                (name, check_type, target, server_id, schedule_type,
                 interval_minutes, cron_expression, is_active, notify_on_failure)
            VALUES
                (@Name, @CheckType, @Target, @ServerId, @ScheduleType,
                 @IntervalMinutes, @CronExpression, @IsActive, @NotifyOnFailure)
            RETURNING id
            """,
            check);
    }

    public async Task UpdateLastResultAsync(Guid id, string status, string? resultJson)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync(
            """
            UPDATE andrew_schema.scheduled_checks
            SET last_status = @status,
                last_result = @resultJson::jsonb,
                last_checked_at = NOW(),
                updated_at = NOW()
            WHERE id = @id
            """,
            new { id, status, resultJson = resultJson ?? "{}" });
    }

    public async Task SetActiveAsync(Guid id, bool active)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync(
            "UPDATE andrew_schema.scheduled_checks SET is_active = @active, updated_at = NOW() WHERE id = @id",
            new { id, active });
    }

    public async Task DeleteAsync(Guid id)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync(
            "DELETE FROM andrew_schema.scheduled_checks WHERE id = @id",
            new { id });
    }

    public async Task LogResultAsync(Guid checkId, string status, string? detailsJson, int durationMs)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync(
            """
            INSERT INTO andrew_schema.check_results (check_id, status, details, duration_ms)
            VALUES (@checkId, @status, @detailsJson::jsonb, @durationMs)
            """,
            new { checkId, status, detailsJson = detailsJson ?? "{}", durationMs });

        // Prune: keep only last 100 results per check
        await conn.ExecuteAsync(
            """
            DELETE FROM andrew_schema.check_results
            WHERE check_id = @checkId
              AND id NOT IN (
                  SELECT id FROM andrew_schema.check_results
                  WHERE check_id = @checkId
                  ORDER BY checked_at DESC
                  LIMIT 100
              )
            """,
            new { checkId });
    }

    public async Task<IEnumerable<CheckResult>> GetRecentResultsAsync(Guid checkId, int limit = 20)
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<CheckResult>(
            """
            SELECT id, check_id AS CheckId, status,
                   details::text AS DetailsJson,
                   duration_ms AS DurationMs,
                   checked_at AS CheckedAt
            FROM andrew_schema.check_results
            WHERE check_id = @checkId
            ORDER BY checked_at DESC
            LIMIT @limit
            """,
            new { checkId, limit });
    }
}
