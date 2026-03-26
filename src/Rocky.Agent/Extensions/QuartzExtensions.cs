using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using Rocky.Agent.Jobs;
using Rocky.Agent.Models;
using Rocky.Agent.Services;

namespace Rocky.Agent.Extensions;

public static class QuartzExtensions
{
    public static IServiceCollection AddRockyScheduler(this IServiceCollection services)
    {
        services.AddQuartz(q =>
        {
            q.UseDefaultThreadPool(tp => tp.MaxConcurrency = 20);

            // ── ResultsCleanupJob — daily at 01:00 UTC (03:00 SAST) ───────────
            var cleanupKey = new JobKey("resultsCleanup", "builtin");
            q.AddJob<ResultsCleanupJob>(cleanupKey, j => j
                .WithDescription("Purge check_results older than 48h (daily 03:00 SAST)")
                .StoreDurably());
            q.AddTrigger(t => t
                .ForJob(cleanupKey)
                .WithIdentity("resultsCleanup.trigger", "builtin")
                .WithCronSchedule("0 0 1 * * ?"));   // 01:00 UTC daily

            // ── ServiceDiscoverySync — every 30 minutes ────────────────────────
            var discoveryKey = new JobKey("serviceDiscovery", "builtin");
            q.AddJob<ServiceDiscoverySync>(discoveryKey, j => j
                .WithDescription("Auto-discover containers from andrew_schema (every 30min)")
                .StoreDurably());
            q.AddTrigger(t => t
                .ForJob(discoveryKey)
                .WithIdentity("serviceDiscovery.trigger", "builtin")
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(90))
                .WithSimpleSchedule(s => s
                    .WithIntervalInMinutes(30)
                    .RepeatForever()
                    .WithMisfireHandlingInstructionFireNow()));

            // ServiceCheckJob — template registered so DI can resolve the type
            q.AddJob<ServiceCheckJob>(j => j
                .WithIdentity("serviceCheck.template", "builtin")
                .StoreDurably());
        });

        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
        services.AddSingleton<IRockyJobScheduler, RockyJobScheduler>();

        return services;
    }
}

/// <summary>
/// Manages per-service Quartz job scheduling at runtime.
/// </summary>
public class RockyJobScheduler(ISchedulerFactory schedulerFactory, ILogger<RockyJobScheduler> logger)
    : IRockyJobScheduler
{
    public async Task RefreshJobScheduleAsync(WatchedService service, CancellationToken ct = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);
        var jobKey    = JobKeyFor(service.Id);
        var trigKey   = TriggerKeyFor(service.Id);

        // Remove existing trigger if present
        if (await scheduler.CheckExists(trigKey, ct))
            await scheduler.UnscheduleJob(trigKey, ct);

        if (!service.Enabled)
        {
            logger.LogDebug("[Rocky] Service '{Name}' disabled — job unscheduled", service.Name);
            return;
        }

        var job = JobBuilder.Create<ServiceCheckJob>()
            .WithIdentity(jobKey)
            .WithDescription(service.DisplayName)
            .UsingJobData(ServiceCheckJob.ServiceIdKey, service.Id.ToString())
            .UsingJobData(ServiceCheckJob.ServiceNameKey, service.Name)
            .StoreDurably()
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity(trigKey)
            .ForJob(jobKey)
            .StartAt(DateTimeOffset.UtcNow.AddSeconds(5))
            .WithSimpleSchedule(s => s
                .WithIntervalInSeconds(service.IntervalSeconds)
                .RepeatForever()
                .WithMisfireHandlingInstructionFireNow())
            .Build();

        if (await scheduler.CheckExists(jobKey, ct))
            await scheduler.AddJob(job, replace: true, ct);
        else
            await scheduler.ScheduleJob(job, trigger, ct);

        logger.LogDebug("[Rocky] Scheduled job for service '{Name}' every {Seconds}s",
            service.Name, service.IntervalSeconds);
    }

    public async Task UnscheduleServiceAsync(Guid serviceId, CancellationToken ct = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);
        var jobKey    = JobKeyFor(serviceId);
        var trigKey   = TriggerKeyFor(serviceId);

        if (await scheduler.CheckExists(trigKey, ct))
            await scheduler.UnscheduleJob(trigKey, ct);
        if (await scheduler.CheckExists(jobKey, ct))
            await scheduler.DeleteJob(jobKey, ct);
    }

    private static JobKey     JobKeyFor(Guid id) => new($"svc.{id}", "services");
    private static TriggerKey TriggerKeyFor(Guid id) => new($"svc.{id}.trigger", "services");
}
