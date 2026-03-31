using Mediahost.Agents.Data;
using Mediahost.Agents.Extensions;
using Mediahost.Agents.Services;
using Mediahost.Agents.Tools;
using Quartz;
using Sam.Agent.Data;
using Sam.Agent.Data.Repositories;
using Sam.Agent.Jobs;
using Sam.Agent.Services;
using Sam.Agent.Tools;

namespace Sam.Agent.Extensions;

public static class SamServiceExtensions
{
    public static IServiceCollection AddSamServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Shared infrastructure (DB, Redis, LLM, vault) ────────────────────────
        services.AddAgentInfrastructure(configuration, "sam", ["/sam", "/databases"]);

        // ── Sam-specific memory ───────────────────────────────────────────────────
        services.AddScoped<SamMemoryService>();
        services.AddScoped<IAgentMemoryService>(sp => sp.GetRequiredService<SamMemoryService>());

        // ── Repositories ──────────────────────────────────────────────────────────
        services.AddScoped<DatabaseRepository>();
        services.AddScoped<ConnectionStatsRepository>();
        services.AddScoped<TableStatsRepository>();
        services.AddScoped<SlowQueryRepository>();
        services.AddScoped<ReplicationRepository>();
        services.AddScoped<DiscoveryLogRepository>();

        // ── Scan services ─────────────────────────────────────────────────────────
        services.AddScoped<MySqlScanService>();
        services.AddScoped<PostgreSqlScanService>();

        // ── Shared tool modules ───────────────────────────────────────────────────
        services.AddScoped<IToolModule, AgentMessagingModule>();

        // ── Tool executor and agent service ───────────────────────────────────────
        services.AddScoped<SamToolExecutor>();
        services.AddScoped<SamAgentService>();
        services.AddScoped<IAgentService>(sp => sp.GetRequiredService<SamAgentService>());

        // ── Quartz jobs ───────────────────────────────────────────────────────────
        services.AddQuartz(q =>
        {
            var scanJob = new JobKey("DatabaseScanJob");
            q.AddJob<DatabaseScanJob>(scanJob, j => j.StoreDurably());
            q.AddTrigger(t => t.ForJob(scanJob)
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(90))
                .WithSimpleSchedule(s => s.WithIntervalInHours(1).RepeatForever()));

            var replJob = new JobKey("ReplicationCheckJob");
            q.AddJob<ReplicationCheckJob>(replJob, j => j.StoreDurably());
            q.AddTrigger(t => t.ForJob(replJob)
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(120))
                .WithSimpleSchedule(s => s.WithIntervalInMinutes(5).RepeatForever()));
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }
}
