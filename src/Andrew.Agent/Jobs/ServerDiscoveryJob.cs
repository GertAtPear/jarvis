using Andrew.Agent.Data.Repositories;
using Andrew.Agent.Services;
using Quartz;

namespace Andrew.Agent.Jobs;

/// <summary>
/// Runs discovery on all registered servers on a configurable interval
/// (default: every 4 hours, set via Andrew:Discovery:IntervalHours).
/// Routes to SSH or WinRM based on each server's connection_type tag.
/// </summary>
[DisallowConcurrentExecution]
public class ServerDiscoveryJob(
    ServerRepository servers,
    ISshDiscoveryService discovery,
    IWindowsDiscoveryService windowsDiscovery,
    IConfiguration config,
    ILogger<ServerDiscoveryJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var maxParallel = config.GetValue<int>("Andrew:Ssh:MaxParallelConnections", 5);

        var allServers = (await servers.GetAllAsync()).ToList();
        var eligible = allServers
            .Where(s => s.Status is not ("pending_credentials" or "decommissioned"))
            .ToList();

        if (eligible.Count == 0)
        {
            logger.LogInformation(
                "Andrew is ready. No servers registered yet. " +
                "Tell Jarvis: 'Andrew, register server <hostname> at <ip>'");
            return;
        }

        logger.LogInformation("Starting scheduled discovery for {Count}/{Total} servers",
            eligible.Count, allServers.Count);

        var semaphore = new SemaphoreSlim(maxParallel, maxParallel);
        var results = new System.Collections.Concurrent.ConcurrentBag<(string hostname, bool success)>();

        var tasks = eligible.Select(async server =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var result = ServerTagHelper.GetConnectionType(server) == "winrm"
                    ? await windowsDiscovery.DiscoverServerAsync(server, ct)
                    : await discovery.DiscoverServerAsync(server, ct);
                results.Add((server.Hostname, result.Success));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Discovery failed for {Host}", server.Hostname);
                results.Add((server.Hostname, false));
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        var online = results.Count(r => r.success);
        var failed = results.Count(r => !r.success);

        logger.LogInformation(
            "Discovery complete: {Online}/{Total} servers online, {Failed} failed",
            online, eligible.Count, failed);

        if (failed > 0)
        {
            var failedHosts = string.Join(", ", results.Where(r => !r.success).Select(r => r.hostname));
            logger.LogWarning("Offline/unreachable servers: {Hosts}", failedHosts);
        }
    }
}
