using Andrew.Agent.Jobs;
using Andrew.Agent.Models;
using Quartz;

namespace Andrew.Agent.Services;

/// <summary>
/// Singleton service that manages Quartz job scheduling for user-defined checks.
/// Provides create/update/delete of scheduled checks at runtime without restart.
/// </summary>
public class JobSchedulerService(
    ISchedulerFactory schedulerFactory,
    ILogger<JobSchedulerService> logger)
{
    private const string CustomGroup = "custom";
    private IScheduler? _scheduler;

    private async Task<IScheduler> GetSchedulerAsync(CancellationToken ct = default) =>
        _scheduler ??= await schedulerFactory.GetScheduler(ct);

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads all active checks from the provided list and schedules them.
    /// Called once at startup after the scheduler is running.
    /// </summary>
    public async Task LoadChecksAsync(IEnumerable<ScheduledCheck> checks, CancellationToken ct = default)
    {
        var loaded = 0;
        foreach (var check in checks)
        {
            try
            {
                await ScheduleCheckAsync(check, ct);
                loaded++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to schedule check '{Name}' on startup", check.Name);
            }
        }
        logger.LogInformation("Loaded {Count} scheduled check(s) from database", loaded);
    }

    /// <summary>
    /// Schedules (or reschedules) a check. Safe to call on create or update.
    /// </summary>
    public async Task ScheduleCheckAsync(ScheduledCheck check, CancellationToken ct = default)
    {
        var scheduler = await GetSchedulerAsync(ct);
        var jobKey = JobKeyFor(check.Id);

        // Remove existing job if present (allows update)
        if (await scheduler.CheckExists(jobKey, ct))
            await scheduler.DeleteJob(jobKey, ct);

        var job = JobBuilder.Create<CustomCheckJob>()
            .WithIdentity(jobKey)
            .UsingJobData(CustomCheckJob.CheckIdKey, check.Id.ToString())
            .WithDescription(check.Name)
            .StoreDurably()
            .Build();

        var trigger = BuildTrigger(check, jobKey);
        await scheduler.ScheduleJob(job, trigger, ct);

        logger.LogInformation(
            "Scheduled check '{Name}' [{Type} → {Target}] {Schedule}",
            check.Name, check.CheckType, check.Target, check.ScheduleSummary);
    }

    /// <summary>Removes a scheduled check from Quartz (does not touch the DB).</summary>
    public async Task<bool> UnscheduleCheckAsync(Guid checkId, CancellationToken ct = default)
    {
        var scheduler = await GetSchedulerAsync(ct);
        var jobKey = JobKeyFor(checkId);

        if (!await scheduler.CheckExists(jobKey, ct))
            return false;

        await scheduler.DeleteJob(jobKey, ct);
        logger.LogInformation("Removed scheduled check {Id} from scheduler", checkId);
        return true;
    }

    /// <summary>Returns true if a check with the given ID is currently scheduled.</summary>
    public async Task<bool> IsScheduledAsync(Guid checkId, CancellationToken ct = default)
    {
        var scheduler = await GetSchedulerAsync(ct);
        return await scheduler.CheckExists(JobKeyFor(checkId), ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static JobKey JobKeyFor(Guid checkId) =>
        new($"check.{checkId}", CustomGroup);

    private static ITrigger BuildTrigger(ScheduledCheck check, JobKey jobKey)
    {
        var triggerKey = new TriggerKey($"check.{check.Id}.trigger", CustomGroup);

        var builder = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey);

        if (check.ScheduleType == "cron" && !string.IsNullOrEmpty(check.CronExpression))
        {
            return builder
                .WithCronSchedule(check.CronExpression, c => c
                    .InTimeZone(TimeZoneInfo.Utc)
                    .WithMisfireHandlingInstructionFireAndProceed())
                .Build();
        }
        else
        {
            var minutes = check.IntervalMinutes ?? 10;
            return builder
                .StartNow()
                .WithSimpleSchedule(s => s
                    .WithIntervalInMinutes(minutes)
                    .RepeatForever()
                    .WithMisfireHandlingInstructionFireNow())
                .Build();
        }
    }
}
