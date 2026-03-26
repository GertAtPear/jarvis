using Dapper;
using Mediahost.Agents.Data;
using Eve.Agent.Models;

namespace Eve.Agent.Data.Repositories;

public class ReminderRepository(DbConnectionFactory db)
{
    private const string SelectColumns = """
        id,
        title,
        description,
        reminder_type       AS ReminderType,
        due_date            AS DueDate,
        due_time            AS DueTime,
        recur_month         AS RecurMonth,
        recur_day           AS RecurDay,
        person_name         AS PersonName,
        tags::text          AS TagsJson,
        status,
        last_triggered      AS LastTriggered,
        snooze_until        AS SnoozeUntil,
        calendar_event_id   AS CalendarEventId,
        notes,
        created_at          AS CreatedAt,
        updated_at          AS UpdatedAt
        """;

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<IEnumerable<Reminder>> GetDueTodayAsync(DateOnly today)
    {
        await using var conn = db.Create();
        // Covers all four reminder types with a single query
        const string sql = """
            SELECT {0}
            FROM eve_schema.reminders
            WHERE status NOT IN ('done','dismissed')
              AND (
                -- Once: exact date match
                (reminder_type = 'once'    AND due_date = @today AND (snooze_until IS NULL OR snooze_until <= @today))
                OR
                -- Yearly: same month and day (birthday/anniversary)
                (reminder_type = 'yearly'  AND recur_month = @month AND recur_day = @day)
                OR
                -- Monthly: same day of month
                (reminder_type = 'monthly' AND recur_day = @day)
                OR
                -- Weekly: recur_day matches current day-of-week (0=Sun … 6=Sat)
                (reminder_type = 'weekly'  AND recur_day = @dow)
              )
            ORDER BY reminder_type, title
            """;
        return await conn.QueryAsync<Reminder>(
            string.Format(sql, SelectColumns),
            new { today, month = today.Month, day = today.Day, dow = (int)today.DayOfWeek });
    }

    public async Task<IEnumerable<Reminder>> GetDueTomorrowAsync(DateOnly tomorrow)
    {
        await using var conn = db.Create();
        const string sql = """
            SELECT {0}
            FROM eve_schema.reminders
            WHERE status NOT IN ('done','dismissed')
              AND (
                (reminder_type = 'once'    AND due_date = @tomorrow)
                OR
                (reminder_type = 'yearly'  AND recur_month = @month AND recur_day = @day)
                OR
                (reminder_type = 'monthly' AND recur_day = @day)
                OR
                (reminder_type = 'weekly'  AND recur_day = @dow)
              )
            ORDER BY reminder_type, title
            """;
        return await conn.QueryAsync<Reminder>(
            string.Format(sql, SelectColumns),
            new { tomorrow, month = tomorrow.Month, day = tomorrow.Day, dow = (int)tomorrow.DayOfWeek });
    }

    public async Task<IEnumerable<Reminder>> GetUpcomingAsync(int days = 7)
    {
        await using var conn = db.Create();
        // For "once" reminders: due_date within the window
        // For recurring: those that will fire within the next `days` days
        var today = DateOnly.FromDateTime(DateTime.Today);
        var until = today.AddDays(days);

        const string sql = """
            SELECT {0}
            FROM eve_schema.reminders
            WHERE status NOT IN ('done','dismissed')
              AND (
                (reminder_type = 'once' AND due_date BETWEEN @today AND @until)
                OR reminder_type IN ('yearly','monthly','weekly')
              )
            ORDER BY due_date NULLS LAST, title
            LIMIT 50
            """;
        return await conn.QueryAsync<Reminder>(
            string.Format(sql, SelectColumns),
            new { today, until });
    }

    public async Task<IEnumerable<Reminder>> GetAllActiveAsync()
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<Reminder>(
            $"SELECT {SelectColumns} FROM eve_schema.reminders WHERE status = 'active' ORDER BY created_at DESC");
    }

    public async Task<IEnumerable<Reminder>> GetByPersonAsync(string personName)
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<Reminder>(
            $"SELECT {SelectColumns} FROM eve_schema.reminders WHERE person_name ILIKE @pattern AND status != 'done' ORDER BY created_at DESC",
            new { pattern = $"%{personName}%" });
    }

    public async Task<Reminder?> GetByIdAsync(Guid id)
    {
        await using var conn = db.Create();
        return await conn.QuerySingleOrDefaultAsync<Reminder>(
            $"SELECT {SelectColumns} FROM eve_schema.reminders WHERE id = @id",
            new { id });
    }

    // ── Mutations ─────────────────────────────────────────────────────────────

    public async Task<Guid> UpsertAsync(Reminder reminder)
    {
        await using var conn = db.Create();
        const string sql = """
            INSERT INTO eve_schema.reminders
                (id, title, description, reminder_type, due_date, due_time,
                 recur_month, recur_day, person_name, tags, status,
                 calendar_event_id, notes)
            VALUES
                (@Id, @Title, @Description, @ReminderType,
                 @DueDate, @DueTime,
                 @RecurMonth, @RecurDay, @PersonName,
                 @TagsJson::jsonb, @Status,
                 @CalendarEventId, @Notes)
            ON CONFLICT (id) DO UPDATE SET
                title             = EXCLUDED.title,
                description       = EXCLUDED.description,
                reminder_type     = EXCLUDED.reminder_type,
                due_date          = EXCLUDED.due_date,
                due_time          = EXCLUDED.due_time,
                recur_month       = EXCLUDED.recur_month,
                recur_day         = EXCLUDED.recur_day,
                person_name       = EXCLUDED.person_name,
                tags              = EXCLUDED.tags,
                status            = EXCLUDED.status,
                calendar_event_id = EXCLUDED.calendar_event_id,
                notes             = EXCLUDED.notes,
                updated_at        = NOW()
            RETURNING id
            """;

        var effectiveId = reminder.Id == Guid.Empty ? Guid.NewGuid() : reminder.Id;

        return await conn.ExecuteScalarAsync<Guid>(sql, new
        {
            Id = effectiveId,
            reminder.Title,
            reminder.Description,
            reminder.ReminderType,
            reminder.DueDate,
            reminder.DueTime,
            reminder.RecurMonth,
            reminder.RecurDay,
            reminder.PersonName,
            TagsJson = reminder.TagsJson ?? "null",
            reminder.Status,
            reminder.CalendarEventId,
            reminder.Notes
        });
    }

    public async Task MarkDoneAsync(Guid id)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync(
            "UPDATE eve_schema.reminders SET status = 'done', last_triggered = NOW(), updated_at = NOW() WHERE id = @id",
            new { id });
    }

    public async Task SnoozeAsync(Guid id, DateOnly until)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync(
            "UPDATE eve_schema.reminders SET status = 'snoozed', snooze_until = @until, updated_at = NOW() WHERE id = @id",
            new { id, until });
    }

    public async Task SetCalendarEventIdAsync(Guid id, string calendarEventId)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync(
            "UPDATE eve_schema.reminders SET calendar_event_id = @calendarEventId, updated_at = NOW() WHERE id = @id",
            new { id, calendarEventId });
    }

    public async Task<int> GetOverdueCountAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        await using var conn = db.Create();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM eve_schema.reminders WHERE reminder_type = 'once' AND due_date < @today AND status = 'active'",
            new { today });
    }

    public async Task<int> GetActiveCountAsync()
    {
        await using var conn = db.Create();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM eve_schema.reminders WHERE status = 'active'");
    }
}
