using Andrew.Agent.Jobs;
using Quartz;

namespace Andrew.Agent.Extensions;

public static class QuartzExtensions
{
    public static IServiceCollection AddAndrewScheduler(
        this IServiceCollection services, IConfiguration config)
    {
        var discoveryIntervalHours = config.GetValue<int>("Andrew:Discovery:IntervalHours", 4);

        services.AddQuartz(q =>
        {
            q.UseDefaultThreadPool(tp => tp.MaxConcurrency = 10);

            // ── ServerDiscoveryJob ─────────────────────────────────────────
            // Starts 60s after boot, then repeats every N hours (configurable).
            var discoveryJobKey = new JobKey("serverDiscovery", "builtin");
            q.AddJob<ServerDiscoveryJob>(discoveryJobKey, j => j
                .WithDescription($"SSH discovery of all servers (every {discoveryIntervalHours}h)")
                .StoreDurably());
            q.AddTrigger(t => t
                .ForJob(discoveryJobKey)
                .WithIdentity("serverDiscovery.trigger", "builtin")
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(60))
                .WithSimpleSchedule(s => s
                    .WithIntervalInHours(discoveryIntervalHours)
                    .RepeatForever()
                    .WithMisfireHandlingInstructionFireNow()));

            // ── NetworkStatusJob ───────────────────────────────────────────
            // Starts 15s after boot, then every 5 minutes.
            var networkJobKey = new JobKey("networkStatus", "builtin");
            q.AddJob<NetworkStatusJob>(networkJobKey, j => j
                .WithDescription("VPN/internet/DNS health checks (every 5min)")
                .StoreDurably());
            q.AddTrigger(t => t
                .ForJob(networkJobKey)
                .WithIdentity("networkStatus.trigger", "builtin")
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(15))
                .WithSimpleSchedule(s => s
                    .WithIntervalInMinutes(5)
                    .RepeatForever()
                    .WithMisfireHandlingInstructionFireNow()));

            // CustomCheckJob is not registered here — instances are created
            // dynamically at runtime by JobSchedulerService.ScheduleCheckAsync.
            // We register the job class so DI can resolve it.
            q.AddJob<CustomCheckJob>(j => j
                .WithIdentity("customCheck.template", "builtin")
                .StoreDurably());
        });

        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }
}
