using Mediahost.Auth.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mediahost.Auth.Middleware;

public class JarvisAuthMiddleware(
    RequestDelegate next,
    ILogger<JarvisAuthMiddleware> logger)
{
    private static readonly HashSet<string> ExemptPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health",
        "/api/auth/login",
    };

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Exempt health check and login
        if (ExemptPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
        if (authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
        {
            var token   = authHeader["Bearer ".Length..].Trim();
            var auth    = context.RequestServices.GetRequiredService<AuthService>();
            var user    = await auth.ValidateTokenAsync(token);

            if (user != null)
            {
                context.Items["User"]  = user;
                context.Items["Token"] = token;
                await next(context);
                return;
            }
        }

        context.Response.StatusCode  = 401;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"error\":\"Unauthorized. Please log in.\"}");
    }
}
