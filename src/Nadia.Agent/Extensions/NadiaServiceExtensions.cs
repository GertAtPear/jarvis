using Mediahost.Agents.Data;
using Mediahost.Agents.Extensions;
using Mediahost.Agents.Services;
using Mediahost.Agents.Tools;
using Nadia.Agent.Data;
using Nadia.Agent.Data.Repositories;
using Nadia.Agent.Jobs;
using Nadia.Agent.Services;
using Nadia.Agent.Tools;
using Quartz;

namespace Nadia.Agent.Extensions;

public static class NadiaServiceExtensions
{
    public static IServiceCollection AddNadiaServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAgentInfrastructure(configuration, "nadia", ["/network"]);

        services.AddScoped<IAgentMemoryService, NadiaMemoryService>();

        services.AddScoped<NetworkInterfaceRepository>();
        services.AddScoped<LatencyRepository>();
        services.AddScoped<WifiNodeRepository>();
        services.AddScoped<DnsCheckRepository>();
        services.AddScoped<FailoverRepository>();

        services.AddScoped<LatencyProbeService>();
        services.AddScoped<DnsHealthService>();
        services.AddSingleton<FailoverDetectionService>();

        services.AddScoped<IToolModule, AgentMessagingModule>();

        services.AddScoped<IAgentToolExecutor, NadiaToolExecutor>();
        services.AddScoped<NadiaAgentService>();

        services.AddQuartz(q =>
        {
            var latencyJob = new JobKey("LatencyMonitorJob");
            q.AddJob<LatencyMonitorJob>(latencyJob, j => j.StoreDurably());
            q.AddTrigger(t => t.ForJob(latencyJob)
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(15))
                .WithSimpleSchedule(s => s.WithIntervalInMinutes(5).RepeatForever()));

            var dnsJob = new JobKey("DnsHealthJob");
            q.AddJob<DnsHealthJob>(dnsJob, j => j.StoreDurably());
            q.AddTrigger(t => t.ForJob(dnsJob)
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(20))
                .WithSimpleSchedule(s => s.WithIntervalInMinutes(5).RepeatForever()));

            var failoverJob = new JobKey("FailoverDetectionJob");
            q.AddJob<FailoverDetectionJob>(failoverJob, j => j.StoreDurably());
            q.AddTrigger(t => t.ForJob(failoverJob)
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(10))
                .WithSimpleSchedule(s => s.WithIntervalInMinutes(1).RepeatForever()));
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }
}
