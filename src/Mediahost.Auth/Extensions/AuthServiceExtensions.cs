using Mediahost.Auth.Middleware;
using Mediahost.Auth.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Mediahost.Auth.Extensions;

public static class AuthServiceExtensions
{
    public static IServiceCollection AddMediahostAuth(this IServiceCollection services)
    {
        services.AddScoped<PasswordHasher>();
        services.AddScoped<UserRepository>();
        services.AddScoped<AuthService>();
        return services;
    }

    public static IApplicationBuilder UseJarvisAuth(this IApplicationBuilder app)
    {
        app.UseMiddleware<JarvisAuthMiddleware>();
        return app;
    }
}
