using Mediahost.Tools.Docker;
using Mediahost.Tools.Http;
using Mediahost.Tools.Interfaces;
using Mediahost.Tools.Ping;
using Mediahost.Tools.Sql;
using Mediahost.Tools.Ssh;
using Mediahost.Tools.WinRm;
using Microsoft.Extensions.DependencyInjection;

namespace Mediahost.Tools.Extensions;

public static class ToolsServiceExtensions
{
    /// <summary>
    /// Registers all Mediahost.Tools execution tools.
    /// Call this once from each agent's DI setup.
    /// </summary>
    public static IServiceCollection AddMediahostTools(this IServiceCollection services)
    {
        // Tools are stateless — Transient matches the connection-per-call model.
        services.AddTransient<ISshTool, SshTool>();
        services.AddTransient<IWinRmTool, WinRmTool>();
        services.AddTransient<IDockerTool, DockerTool>();
        services.AddTransient<ISqlTool, SqlTool>();
        services.AddTransient<IPingTool, PingTool>();
        services.AddTransient<IHttpCheckTool, HttpCheckTool>();

        // Named HttpClient used by HttpCheckTool.
        // Fail fast — no retries for health checks.
        services.AddHttpClient("healthcheck", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Add("User-Agent", "Mediahost-Tools/1.0");
        });

        return services;
    }
}
