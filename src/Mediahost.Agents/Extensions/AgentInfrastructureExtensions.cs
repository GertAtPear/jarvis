using Mediahost.Agents.Alerting;
using Mediahost.Agents.Data;
using Mediahost.Llm.Extensions;
using Mediahost.Shared.Services;
using Mediahost.Vault.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using StackExchange.Redis;

namespace Mediahost.Agents.Extensions;

public static class AgentInfrastructureExtensions
{
    /// <summary>
    /// Registers the shared infrastructure every agent needs:
    /// DbConnectionFactory, NpgsqlDataSource, Redis, the full LLM layer, and a scoped vault.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The app configuration (for connection strings).</param>
    /// <param name="agentName">Lowercase agent identifier used in LLM routing (e.g. "andrew").</param>
    /// <param name="vaultOwnedPrefixes">Vault path prefixes this agent owns (e.g. ["/servers"]).</param>
    public static IServiceCollection AddAgentInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string agentName,
        string[] vaultOwnedPrefixes)
    {
        // ── DbConnectionFactory — used by AgentMemoryService subclasses ───────────
        services.AddSingleton<DbConnectionFactory>();

        // ── NpgsqlDataSource — required by LlmService (model routing + usage log) ──
        var connStr = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required");
        var dataSource = new NpgsqlDataSourceBuilder(connStr).Build();
        services.AddSingleton(dataSource);
        services.AddSingleton<NpgsqlDataSource>(dataSource);

        // ── Redis — required by TaskClassifierService (classification cache) ─────
        var redisConn = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is required");
        services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(redisConn));

        // ── LLM layer (all providers + routing matrix) ────────────────────────────
        services.AddMediahostLlm();

        // ── Scoped vault — agent may only access its own prefixes ─────────────────
        services.AddScoped<IScopedVaultService>(sp => new ScopedVaultService(
            sp.GetRequiredService<IVaultService>(),
            agentName: agentName,
            ownedPrefixes: vaultOwnedPrefixes,
            sp.GetRequiredService<ILogger<ScopedVaultService>>()));

        // ── Alert dispatch — shared across all agents ─────────────────────────
        services.AddHttpClient();
        services.AddScoped<IAlertDispatchService, AlertDispatchService>();

        return services;
    }
}
