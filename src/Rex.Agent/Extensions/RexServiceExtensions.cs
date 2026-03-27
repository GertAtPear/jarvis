using Mediahost.Agents.Extensions;
using Mediahost.Agents.Services;
using Mediahost.Agents.Tools;
using Rex.Agent.Data;
using Rex.Agent.Data.Repositories;
using Rex.Agent.Services;
using Rex.Agent.Tools;

namespace Rex.Agent.Extensions;

public static class RexServiceExtensions
{
    public static IServiceCollection AddRexServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Shared infrastructure (DB, Redis, LLM, vault) ────────────────────────
        services.AddAgentInfrastructure(configuration, "rex", ["/rex"]);

        // ── Rex-specific memory ───────────────────────────────────────────────────
        services.AddScoped<RexMemoryService>();

        // ── Repositories ──────────────────────────────────────────────────────────
        services.AddScoped<ProjectRepository>();
        services.AddScoped<DevSessionRepository>();
        services.AddScoped<ScaffoldingSessionRepository>();
        services.AddScoped<ScaffoldedAgentRepository>();
        services.AddScoped<PortRegistryRepository>();
        services.AddScoped<AgentUpdateRepository>();

        // ── Services ──────────────────────────────────────────────────────────────
        services.AddScoped<GitService>();
        services.AddScoped<GitHubService>();
        services.AddSingleton<ContainerService>();
        services.AddScoped<DeveloperAgentService>();
        services.AddScoped<ScaffoldingPlanService>();
        services.AddScoped<AgentScaffoldingService>();
        services.AddScoped<AgentMetadataService>();
        services.AddScoped<AgentCodeUpdateService>();

        // ── Laptop + Browser tools ────────────────────────────────────────────────
        services.AddScoped<IToolModule, LaptopModule>();
        services.AddScoped<IToolModule, BrowserModule>();

        // ── Tool executor and agent service ───────────────────────────────────────
        services.AddScoped<RexToolExecutor>();
        services.AddScoped<IAgentService, RexAgentService>();

        return services;
    }
}
