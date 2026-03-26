namespace Eve.Agent.Models;

public class Reminder
{
    public Guid      Id             { get; init; }
    public string    Title          { get; init; } = "";
    public string?   Description    { get; init; }
    public string    ReminderType   { get; init; } = "once";  // once|yearly|monthly|weekly
    public DateOnly? DueDate        { get; init; }
    public TimeOnly? DueTime        { get; init; }
    public int?      RecurMonth     { get; init; }  // 1-12 for yearly
    public int?      RecurDay       { get; init; }  // day-of-month or day-of-week (0=Sun)
    public string?   PersonName     { get; init; }
    public string?   TagsJson       { get; init; }
    public string    Status         { get; init; } = "active";  // active|snoozed|done|dismissed
    public DateTimeOffset? LastTriggered  { get; init; }
    public DateOnly? SnoozeUntil   { get; init; }
    public string?   CalendarEventId { get; init; }
    public string?   Notes          { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
