using Andrew.Agent.Data;
using Andrew.Agent.Jobs.CheckExecutors;
using Andrew.Agent.Services;
using Andrew.Agent.Tools;
using Mediahost.Agents.Extensions;
using Mediahost.Agents.Services;
using Mediahost.Agents.Tools;
using Mediahost.Agents.Capabilities;

namespace Andrew.Agent.Extensions;

public static class AgentServiceExtensions
{
    public static IServiceCollection AddAgentServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Shared infrastructure (DB, Redis, LLM, vault) ────────────────────────
        services.AddAgentInfrastructure(configuration, "andrew", ["/servers"]);

        // ── Mediahost.Agents capabilities + underlying tools ─────────────────────
        services.AddMediahostAgents();

        // ── Andrew-specific memory ────────────────────────────────────────────────
        services.AddScoped<AndrewMemoryService>();

        // ── Scheduler service — singleton so it holds a stable IScheduler ref ────
        services.AddSingleton<JobSchedulerService>();

        // ── Check executors (scoped — use scoped repos + HttpClient) ─────────────
        services.AddScoped<ContainerCheckExecutor>();
        services.AddScoped<ServerUpCheckExecutor>();
        services.AddScoped<WebsiteUpCheckExecutor>();

        // ── Discovery services ────────────────────────────────────────────────────
        services.AddScoped<ISshDiscoveryService, SshDiscoveryService>();
        services.AddScoped<IWindowsDiscoveryService, WindowsDiscoveryService>();
        services.AddScoped<WinRmService>();
        services.AddScoped<ServerRegistrationService>();

        // ── Shared tool modules (compose tools available to AndrewToolExecutor) ───
        services.AddScoped<IServerResolver, AndrewServerResolver>();
        services.AddScoped<IToolModule, NetworkDiagnosticsModule>();
        services.AddScoped<IToolModule, RemoteExecModule>();
        services.AddScoped<IToolModule, BrowserModule>();
        services.AddScoped<IToolModule, LaptopModule>();

        // ── Tool executor (scoped — receives IEnumerable<IToolModule> from DI) ────
        services.AddScoped<AndrewToolExecutor>();

        // ── Agent service (scoped) ────────────────────────────────────────────────
        services.AddScoped<IAgentService, AndrewAgentService>();

        return services;
    }
}
