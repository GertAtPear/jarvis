using Nadia.Agent.Services;
using Quartz;

namespace Nadia.Agent.Jobs;

[DisallowConcurrentExecution]
public class LatencyMonitorJob(LatencyProbeService probeService, ILogger<LatencyMonitorJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try { await probeService.ProbeAllInterfacesAsync(context.CancellationToken); }
        catch (Exception ex) { logger.LogError(ex, "[Nadia] LatencyMonitorJob failed"); }
    }
}
