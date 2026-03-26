using Eve.Agent.Data;
using Eve.Agent.Services;
using Eve.Agent.Tools;
using Mediahost.Agents.Extensions;
using Mediahost.Agents.Services;

namespace Eve.Agent.Extensions;

public static class EveServiceExtensions
{
    public static IServiceCollection AddEveServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Shared infrastructure (DB, Redis, LLM, vault) ────────────────────────
        services.AddAgentInfrastructure(configuration, "eve", ["/eve"]);

        // ── Eve-specific memory ───────────────────────────────────────────────────
        services.AddScoped<EveMemoryService>();

        // ── Core services ─────────────────────────────────────────────────────────
        services.AddScoped<MorningBriefingGeneratorService>();
        services.AddScoped<GoogleCalendarService>();

        // ── Tool executor ─────────────────────────────────────────────────────────
        services.AddScoped<EveToolExecutor>();

        // ── Agent service ─────────────────────────────────────────────────────────
        services.AddScoped<IAgentService, EveAgentService>();

        return services;
    }
}
