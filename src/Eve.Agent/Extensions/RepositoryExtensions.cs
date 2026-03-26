using Eve.Agent.Data.Repositories;

namespace Eve.Agent.Extensions;

public static class RepositoryExtensions
{
    public static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<ReminderRepository>();
        services.AddScoped<ContactRepository>();
        return services;
    }
}
