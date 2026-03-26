using System.Text.Json;
using Eve.Agent.Data;
using Eve.Agent.Data.Repositories;
using Eve.Agent.Models;
using Eve.Agent.Services;
using Mediahost.Agents.Services;
using Mediahost.Llm.Models;

namespace Eve.Agent.Tools;

public class EveToolExecutor(
    ReminderRepository reminders,
    ContactRepository contacts,
    MorningBriefingGeneratorService briefingGenerator,
    GoogleCalendarService calendar,
    EveMemoryService memory,
    ILogger<EveToolExecutor> logger) : IAgentToolExecutor
{
    public IReadOnlyList<ToolDefinition> GetTools() => EveToolDefinitions.GetTools();
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task<string> ExecuteAsync(string toolName, JsonDocument input, CancellationToken ct)
    {
        try
        {
            return toolName switch
            {
                "add_reminder"        => await AddReminderAsync(input, ct),
                "list_reminders"      => await ListRemindersAsync(input, ct),
                "complete_reminder"   => await CompleteReminderAsync(input, ct),
                "snooze_reminder"     => await SnoozeReminderAsync(input, ct),
                "add_contact"         => await AddContactAsync(input, ct),
                "get_briefing"        => await GetBriefingAsync(ct),
                "create_calendar_event" => await CreateCalendarEventAsync(input, ct),
                "remember_fact"         => await RememberFactAsync(input, ct),
                "forget_fact"           => await ForgetFactAsync(input, ct),
                _ => Err($"Unknown tool: {toolName}")
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tool execution failed: {Tool}", toolName);
            return Err(ex.Message);
        }
    }

    // ── Tool implementations ──────────────────────────────────────────────────

    private async Task<string> AddReminderAsync(JsonDocument input, CancellationToken ct)
    {
        var title   = RequireString(input, "title");
        var type    = GetString(input, "reminder_type") ?? "once";
        var desc    = GetString(input, "description");
        var person  = GetString(input, "person_name");
        var notes   = GetString(input, "notes");

        DateOnly? dueDate = null;
        if (GetString(input, "due_date") is { } ds)
            dueDate = DateOnly.Parse(ds);

        int? recurMonth = GetInt(input, "recur_month");
        int? recurDay   = GetInt(input, "recur_day");

        string? tagsJson = null;
        if (input.RootElement.TryGetProperty("tags", out var tagsEl))
            tagsJson = tagsEl.GetRawText();

        var reminder = new Reminder
        {
            Id           = Guid.NewGuid(),
            Title        = title,
            Description  = desc,
            ReminderType = type,
            DueDate      = dueDate,
            RecurMonth   = recurMonth,
            RecurDay     = recurDay,
            PersonName   = person,
            TagsJson     = tagsJson ?? "null",
            Status       = "active",
            Notes        = notes
        };

        var id = await reminders.UpsertAsync(reminder);

        return Ok(new { id, title, reminder_type = type, message = "Reminder saved." });
    }

    private async Task<string> ListRemindersAsync(JsonDocument input, CancellationToken ct)
    {
        var filter = GetString(input, "filter") ?? "today";
        var person = GetString(input, "person_name");

        var today = DateOnly.FromDateTime(DateTime.Today);

        IEnumerable<Reminder> list;

        if (!string.IsNullOrEmpty(person))
        {
            list = await reminders.GetByPersonAsync(person);
        }
        else
        {
            list = filter switch
            {
                "today"    => await reminders.GetDueTodayAsync(today),
                "tomorrow" => await reminders.GetDueTomorrowAsync(today.AddDays(1)),
                "week"     => await reminders.GetUpcomingAsync(7),
                _          => await reminders.GetAllActiveAsync()
            };
        }

        var rows = list.Select(r => new
        {
            id           = r.Id,
            title        = r.Title,
            type         = r.ReminderType,
            due_date     = r.DueDate?.ToString("yyyy-MM-dd"),
            person_name  = r.PersonName,
            status       = r.Status,
            description  = r.Description
        }).ToList();

        return Ok(new { count = rows.Count, filter, reminders = rows });
    }

    private async Task<string> CompleteReminderAsync(JsonDocument input, CancellationToken ct)
    {
        var id = Guid.Parse(RequireString(input, "reminder_id"));
        await reminders.MarkDoneAsync(id);
        return Ok(new { id, status = "done", message = "Reminder marked as done." });
    }

    private async Task<string> SnoozeReminderAsync(JsonDocument input, CancellationToken ct)
    {
        var id    = Guid.Parse(RequireString(input, "reminder_id"));
        var until = DateOnly.Parse(RequireString(input, "until_date"));
        await reminders.SnoozeAsync(id, until);
        return Ok(new { id, snoozed_until = until.ToString("yyyy-MM-dd"), message = "Reminder snoozed." });
    }

    private async Task<string> AddContactAsync(JsonDocument input, CancellationToken ct)
    {
        var name         = RequireString(input, "name");
        var relationship = GetString(input, "relationship");
        var notes        = GetString(input, "notes");

        DateOnly? birthday    = null;
        DateOnly? anniversary = null;

        if (GetString(input, "birthday") is { } bStr)
        {
            // MM-DD → use a fixed year for storage
            var parts = bStr.Split('-');
            birthday = new DateOnly(2000, int.Parse(parts[0]), int.Parse(parts[1]));
        }

        if (GetString(input, "anniversary") is { } aStr)
        {
            var parts = aStr.Split('-');
            anniversary = new DateOnly(2000, int.Parse(parts[0]), int.Parse(parts[1]));
        }

        var contact = new Contact
        {
            Id           = Guid.NewGuid(),
            Name         = name,
            Relationship = relationship,
            Birthday     = birthday,
            Anniversary  = anniversary,
            Notes        = notes
        };

        await contacts.UpsertAsync(contact);
        return Ok(new { name, message = "Contact saved." });
    }

    private async Task<string> GetBriefingAsync(CancellationToken ct)
    {
        var today    = DateOnly.FromDateTime(DateTime.Today);
        var briefing = await briefingGenerator.GenerateBriefingAsync(today);
        return Ok(new { briefing });
    }

    private async Task<string> CreateCalendarEventAsync(JsonDocument input, CancellationToken ct)
    {
        var title       = RequireString(input, "title");
        var date        = RequireString(input, "date");
        var time        = GetString(input, "time");
        var description = GetString(input, "description");

        var eventId = await calendar.CreateEventAsync(title, date, time, description, ct);

        // Link to reminder if reminder_id provided
        if (GetString(input, "reminder_id") is { } ridStr && Guid.TryParse(ridStr, out var rid))
            await reminders.SetCalendarEventIdAsync(rid, eventId);

        return Ok(new
        {
            calendar_event_id = eventId,
            title,
            date,
            message = "Calendar event created."
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string RequireString(JsonDocument doc, string key)
    {
        if (!doc.RootElement.TryGetProperty(key, out var prop))
            throw new ArgumentException($"Required parameter '{key}' is missing.");
        return prop.GetString() ?? throw new ArgumentException($"Parameter '{key}' must not be null.");
    }

    private static string? GetString(JsonDocument doc, string key) =>
        doc.RootElement.TryGetProperty(key, out var p) ? p.GetString() : null;

    private static int? GetInt(JsonDocument doc, string key) =>
        doc.RootElement.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Number
            ? p.GetInt32()
            : null;

    // ── Permanent memory ──────────────────────────────────────────────────────

    private async Task<string> RememberFactAsync(JsonDocument input, CancellationToken ct)
    {
        var key   = RequireString(input, "key");
        var value = RequireString(input, "value");
        await memory.RememberFactAsync(key, value, ct);
        return Ok(new { remembered = true, key, message = $"I'll remember: {key} = {value}" });
    }

    private async Task<string> ForgetFactAsync(JsonDocument input, CancellationToken ct)
    {
        var key = RequireString(input, "key");
        await memory.ForgetFactAsync(key, ct);
        return Ok(new { forgotten = true, key });
    }

    private static string Ok(object value)  => JsonSerializer.Serialize(value, JsonOpts);
    private static string Err(string msg)   => JsonSerializer.Serialize(new { error = msg }, JsonOpts);
}
