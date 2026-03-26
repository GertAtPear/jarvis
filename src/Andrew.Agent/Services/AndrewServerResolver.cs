using Andrew.Agent.Data.Repositories;
using Mediahost.Agents.Services;
using Mediahost.Shared.Services;

namespace Andrew.Agent.Services;

/// <summary>
/// Resolves a server query (hostname, IP, or display name) to connection details
/// by looking up Andrew's server registry and loading vault secrets.
/// </summary>
public class AndrewServerResolver(
    ServerRepository servers,
    IScopedVaultService vault,
    ILogger<AndrewServerResolver> logger) : IServerResolver
{
    public async Task<ServerConnectionInfo?> ResolveAsync(string serverQuery, CancellationToken ct = default)
    {
        var allServers = await servers.GetAllAsync();
        var server = allServers.FirstOrDefault(s =>
            s.Hostname.Equals(serverQuery, StringComparison.OrdinalIgnoreCase) ||
            s.IpAddress?.Equals(serverQuery, StringComparison.OrdinalIgnoreCase) == true);

        if (server is null)
        {
            logger.LogDebug("AndrewServerResolver: no server found for query '{Query}'", serverQuery);
            return null;
        }

        var vaultPath = $"/servers/{server.Hostname}";
        Dictionary<string, string> secrets;
        try { secrets = await vault.GetSecretsBulkAsync(vaultPath, ct); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AndrewServerResolver: vault lookup failed for {Path}", vaultPath);
            secrets = [];
        }

        var host = string.IsNullOrWhiteSpace(server.IpAddress) ? server.Hostname : server.IpAddress;
        return new ServerConnectionInfo(server.Hostname, host, secrets);
    }
}
