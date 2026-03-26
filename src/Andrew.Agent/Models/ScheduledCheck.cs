namespace Andrew.Agent.Models;

public class ScheduledCheck
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";

    /// <summary>container_running | server_up | website_up | port_listening</summary>
    public string CheckType { get; init; } = "";

    /// <summary>Container name, hostname, URL, or hostname:port depending on CheckType.</summary>
    public string Target { get; init; } = "";

    /// <summary>Optional: restrict container_running checks to a specific server.</summary>
    public Guid? ServerId { get; init; }

    /// <summary>interval | cron</summary>
    public string ScheduleType { get; init; } = "interval";

    /// <summary>For interval schedules — repeat every N minutes.</summary>
    public int? IntervalMinutes { get; init; }

    /// <summary>For cron schedules — Quartz 6-field format, e.g. "0 0 6 * * ?" for daily at 06:00.</summary>
    public string? CronExpression { get; init; }

    public bool IsActive { get; init; } = true;
    public bool NotifyOnFailure { get; init; } = true;

    public DateTime? LastCheckedAt { get; init; }

    /// <summary>ok | failed | unknown</summary>
    public string? LastStatus { get; init; }

    public string? LastResultJson { get; init; }

    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    /// <summary>Human-readable schedule description for display.</summary>
    public string ScheduleSummary => ScheduleType == "cron"
        ? $"cron: {CronExpression}"
        : $"every {IntervalMinutes}min";
}
