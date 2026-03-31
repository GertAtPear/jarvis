using Mediahost.Agents.Data;
using Mediahost.Agents.Extensions;
using Mediahost.Agents.Services;
using Mediahost.Agents.Tools;
using Research.Agent.Data;
using Research.Agent.Data.Repositories;
using Research.Agent.Services;
using Research.Agent.Tools;

namespace Research.Agent.Extensions;

public static class ResearchServiceExtensions
{
    public static IServiceCollection AddResearchServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Shared infrastructure (DB, Redis, LLM, vault) ─────────────────────
        services.AddAgentInfrastructure(configuration, "research", ["/research"]);

        // ── Memory ────────────────────────────────────────────────────────────
        services.AddScoped<ResearchMemoryService>();
        services.AddScoped<IAgentMemoryService>(sp => sp.GetRequiredService<ResearchMemoryService>());

        // ── Repositories ──────────────────────────────────────────────────────
        services.AddScoped<ResearchDatabaseRepository>();

        // ── Shared tool modules ───────────────────────────────────────────────
        services.AddScoped<IToolModule, BrowserModule>();
        services.AddScoped<IToolModule, WorkspaceModule>();
        services.AddScoped<IToolModule, AgentMessagingModule>();

        // ── Tool executor and agent service ───────────────────────────────────
        services.AddScoped<ResearchToolExecutor>();
        services.AddScoped<ResearchAgentService>();
        services.AddScoped<IAgentService>(sp => sp.GetRequiredService<ResearchAgentService>());

        return services;
    }
}
