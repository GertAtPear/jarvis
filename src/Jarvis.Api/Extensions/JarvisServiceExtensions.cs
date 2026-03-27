using Jarvis.Api.Data;
using Jarvis.Api.Services;
using Mediahost.Auth.Extensions;
using Mediahost.Llm.Extensions;
using Mediahost.Vault.Extensions;
using Npgsql;
using StackExchange.Redis;
using AgentsDbFactory = Mediahost.Agents.Data.DbConnectionFactory;

namespace Jarvis.Api.Extensions;


public static class JarvisServiceExtensions
{
    public static IServiceCollection AddJarvisServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Infrastructure ──────────────────────────────────────────────────
        var connStr = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required");

        // NpgsqlDataSource — required by Mediahost.Llm (model selector, usage logger)
        var dataSource = new NpgsqlDataSourceBuilder(connStr).Build();
        services.AddSingleton(dataSource);
        services.AddSingleton<NpgsqlDataSource>(dataSource);

        // Redis — required by Mediahost.Llm (classification cache)
        var redisConn = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is required");
        services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(redisConn));

        // ── Vault ───────────────────────────────────────────────────────────
        services.AddInfisicalVault(configuration);

        // ── LLM layer ───────────────────────────────────────────────────────
        services.AddHttpClient();          // IHttpClientFactory
        services.AddMediahostLlm();

        // ── Data layer ──────────────────────────────────────────────────────
        services.AddSingleton<DbConnectionFactory>();
        services.AddScoped<AgentRegistryRepository>();

        // ── Jarvis services ─────────────────────────────────────────────────
        services.AddSingleton<AgentClientFactory>();
        services.AddScoped<DynamicRoutingService>();
        services.AddScoped<ConversationService>();
        services.AddScoped<AttachmentService>();
        services.AddScoped<MorningBriefingService>();
        services.AddScoped<JarvisOrchestratorService>();
        services.AddScoped<UsageAnalyticsService>();
        services.AddScoped<RoutingRulesService>();

        // ── Device management (Local Agent Host) ─────────────────────────────
        services.AddScoped<DeviceRepository>();
        services.AddSingleton<DeviceConnectionTracker>();
        services.AddSingleton<IDeviceToolForwarder, DeviceToolForwarder>();

        // ── Auth ─────────────────────────────────────────────────────────────
        // Register Mediahost.Agents.Data.DbConnectionFactory for UserRepository
        services.AddSingleton<AgentsDbFactory>();
        services.AddMediahostAuth();

        return services;
    }
}
