using Andrew.Agent.Models;

namespace Andrew.Agent.Services;

public interface IWindowsDiscoveryService
{
    Task<DiscoveryResult> DiscoverServerAsync(ServerInfo server, CancellationToken ct = default);
}
