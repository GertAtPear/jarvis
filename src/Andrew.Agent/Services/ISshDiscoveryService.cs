using Andrew.Agent.Models;

namespace Andrew.Agent.Services;

public interface ISshDiscoveryService
{
    Task<DiscoveryResult> DiscoverServerAsync(ServerInfo server, CancellationToken ct = default);
}
