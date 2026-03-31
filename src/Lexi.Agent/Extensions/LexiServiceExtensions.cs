using Lexi.Agent.Data;
using Lexi.Agent.Data.Repositories;
using Lexi.Agent.Jobs;
using Lexi.Agent.Services;
using Lexi.Agent.Tools;
using Mediahost.Agents.Data;
using Mediahost.Agents.Extensions;
using Mediahost.Agents.Services;
using Mediahost.Agents.Tools;
using Quartz;

namespace Lexi.Agent.Extensions;

public static class LexiServiceExtensions
{
    public static IServiceCollection AddLexiServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAgentInfrastructure(configuration, "lexi", ["/security"]);

        // ── Mediahost.Agents capabilities + underlying tools ─────────────────────
        services.AddMediahostAgents();

        // ── Lexi-specific memory ──────────────────────────────────────────────────
        services.AddScoped<LexiMemoryService>();
        services.AddScoped<IAgentMemoryService>(sp => sp.GetRequiredService<LexiMemoryService>());

        // ── Repositories ──────────────────────────────────────────────────────────
        services.AddScoped<CertificateRepository>();
        services.AddScoped<OpenPortRepository>();
        services.AddScoped<AnomalyRepository>();
        services.AddScoped<CveRepository>();
        services.AddScoped<SoftwareRepository>();
        services.AddScoped<NetworkDeviceRepository>();
        services.AddScoped<ScanLogRepository>();

        // ── Services ──────────────────────────────────────────────────────────────
        services.AddScoped<CertCheckService>();
        services.AddScoped<AccessLogAnalyserService>();
        services.AddScoped<NetworkScanService>();

        // ── Shared tool modules ───────────────────────────────────────────────────
        services.AddScoped<IToolModule, AgentMessagingModule>();

        // ── Tool executor & agent ─────────────────────────────────────────────────
        services.AddScoped<LexiToolExecutor>();
        services.AddScoped<LexiAgentService>();
        services.AddScoped<IAgentService>(sp => sp.GetRequiredService<LexiAgentService>());

        // ── Quartz jobs ───────────────────────────────────────────────────────────
        services.AddQuartz(q =>
        {
            // Access log analysis every 15 min
            var accessLogJob = new JobKey("AccessLogJob");
            q.AddJob<AccessLogJob>(accessLogJob, j => j.StoreDurably());
            q.AddTrigger(t => t.ForJob(accessLogJob)
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(30))
                .WithSimpleSchedule(s => s.WithIntervalInMinutes(15).RepeatForever()));

            // Cert scan daily at 04:00 UTC
            var certScanJob = new JobKey("CertScanJob");
            q.AddJob<CertScanJob>(certScanJob, j => j.StoreDurably());
            q.AddTrigger(t => t.ForJob(certScanJob)
                .WithCronSchedule("0 0 4 * * ?"));

            // Network device scan daily at 02:00 UTC
            var networkScanJob = new JobKey("NetworkDeviceScanJob");
            q.AddJob<NetworkDeviceScanJob>(networkScanJob, j => j.StoreDurably());
            q.AddTrigger(t => t.ForJob(networkScanJob)
                .WithCronSchedule("0 0 2 * * ?"));
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }
}
