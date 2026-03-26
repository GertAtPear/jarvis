using Mediahost.Agents.Capabilities;
using Mediahost.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Mediahost.Agents.Extensions;

public static class AgentsServiceExtensions
{
    /// <summary>
    /// Registers all Mediahost.Agents capability wrappers and the underlying Mediahost.Tools layer.
    /// Agents only need to call this — it brings tools with it.
    /// </summary>
    public static IServiceCollection AddMediahostAgents(this IServiceCollection services)
    {
        // Raw execution tools (Mediahost.Tools)
        services.AddMediahostTools();

        // Capability wrappers — singleton: stateless, safe to share across scopes
        services.AddSingleton<SshCapability>();
        services.AddSingleton<WinRmCapability>();
        services.AddSingleton<DockerCapability>();
        services.AddSingleton<SqlCapability>();
        services.AddSingleton<HttpCapability>();

        return services;
    }
}
