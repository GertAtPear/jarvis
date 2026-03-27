using Lexi.Agent.Services;
using Quartz;

namespace Lexi.Agent.Jobs;

[DisallowConcurrentExecution]
public class NetworkDeviceScanJob(NetworkScanService scanService, ILogger<NetworkDeviceScanJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try { await scanService.ScanAsync(context.CancellationToken); }
        catch (Exception ex) { logger.LogError(ex, "[Lexi] NetworkDeviceScanJob failed"); }
    }
}
