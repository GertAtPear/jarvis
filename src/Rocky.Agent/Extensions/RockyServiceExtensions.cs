using Mediahost.Agents.Extensions;
using Mediahost.Agents.Services;
using Mediahost.Agents.Tools;
using Rocky.Agent.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rocky.Agent.Data.Repositories;
using Rocky.Agent.Jobs;
using Rocky.Agent.Services;

namespace Rocky.Agent.Extensions;

public static class RockyServiceExtensions
{
    public static IServiceCollection AddRockyServices(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        // ── Shared infrastructure (DB, Redis, LLM, vault) ────────────────────
        services.AddAgentInfrastructure(configuration, "rocky", ["/pipelines", "/servers"]);

        // ── Mediahost.Agents capabilities + underlying tools ──────────────────
        services.AddMediahostAgents();

        // ── Data layer ────────────────────────────────────────────────────────
        services.AddScoped<Rocky.Agent.Data.DbConnectionFactory>();
        services.AddScoped<WatchedServiceRepository>();
        services.AddScoped<CheckResultRepository>();
        services.AddScoped<AlertRepository>();

        // ── Quartz scheduler ──────────────────────────────────────────────────
        services.AddRockyScheduler();

        // ── Check executor ────────────────────────────────────────────────────
        services.AddScoped<CheckExecutorService>();

        // ── Shared tool modules ───────────────────────────────────────────────
        services.AddScoped<IToolModule, AgentMessagingModule>();

        // ── Tool executor + agent service ─────────────────────────────────────
        services.AddScoped<RockyToolExecutor>();
        services.AddScoped<IAgentService, RockyAgentService>();

        return services;
    }

    /// <summary>
    /// Called on startup: loads all enabled watched services into Quartz.
    /// </summary>
    public static async Task LoadServiceJobsAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var repo      = scope.ServiceProvider.GetRequiredService<WatchedServiceRepository>();
        var scheduler = scope.ServiceProvider.GetRequiredService<IRockyJobScheduler>();
        var logger    = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            var watchedServices = (await repo.GetAllEnabledAsync()).ToList();
            foreach (var svc in watchedServices)
                await scheduler.RefreshJobScheduleAsync(svc);

            logger.LogInformation("[Rocky] Loaded {Count} service check jobs on startup", watchedServices.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Rocky] Could not load service jobs on startup (DB may not be ready)");
        }
    }
}
