using Dapper;
using Mediahost.Agents.Data;

namespace Eve.Agent.Data;

/// <summary>
/// Eve's memory service. Extends the shared base (sessions, conversations, memory tables)
/// and adds <see cref="LoadDailyContextAsync"/> for today's reminder context.
/// </summary>
public class EveMemoryService : AgentMemoryService
{
    private readonly DbConnectionFactory _db;

    public EveMemoryService(DbConnectionFactory db) : base(db)
    {
        _db = db;
    }

    protected override string Schema => "eve_schema";

    // ── Daily context (today's reminders injected fresh each session) ─────────

    /// <summary>
    /// Returns a summary of today's + this week's reminders to inject as session context.
    /// Returns null if nothing relevant today.
    /// </summary>
    public async Task<string?> LoadDailyContextAsync(CancellationToken ct = default)
    {
        await using var conn = _db.Create();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var weekEnd = today.AddDays(7);

        var todayItems = (await conn.QueryAsync<string>("""
            SELECT title FROM eve_schema.reminders
            WHERE status = 'active'
              AND (
                (reminder_type = 'once'    AND due_date = @today)
                OR (reminder_type = 'yearly'  AND recur_month = @month AND recur_day = @day)
                OR (reminder_type = 'monthly' AND recur_day = @day)
                OR (reminder_type = 'weekly'  AND recur_day = @dow)
              )
            ORDER BY title
            """, new { today, month = today.Month, day = today.Day, dow = (int)today.DayOfWeek })).ToList();

        var weekItems = (await conn.QueryAsync<(string Title, string? DueDate)>("""
            SELECT title, due_date::text FROM eve_schema.reminders
            WHERE status = 'active'
              AND reminder_type = 'once'
              AND due_date > @today AND due_date <= @weekEnd
            ORDER BY due_date, title
            LIMIT 10
            """, new { today, weekEnd })).ToList();

        if (todayItems.Count == 0 && weekItems.Count == 0) return null;

        var lines = new List<string> { $"## Today's context ({today:dddd, d MMMM yyyy})" };
        if (todayItems.Count > 0)
        {
            lines.Add("**Due today:**");
            lines.AddRange(todayItems.Select(t => $"- {t}"));
        }
        if (weekItems.Count > 0)
        {
            lines.Add("**Coming up this week:**");
            lines.AddRange(weekItems.Select(i => $"- {i.DueDate}: {i.Title}"));
        }
        return string.Join("\n", lines);
    }
}
