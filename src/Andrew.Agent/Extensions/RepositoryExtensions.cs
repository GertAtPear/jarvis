using Andrew.Agent.Data.Repositories;
using Mediahost.Agents.Data;

namespace Andrew.Agent.Extensions;

public static class RepositoryExtensions
{
    public static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<ServerRepository>();
        services.AddScoped<ContainerRepository>();
        services.AddScoped<ApplicationRepository>();
        services.AddScoped<NetworkFactRepository>();
        services.AddScoped<DiscoveryLogRepository>();
        services.AddScoped<ScheduledCheckRepository>();

        return services;
    }
}
