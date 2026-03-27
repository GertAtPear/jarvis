using Nadia.Agent.Services;
using Quartz;

namespace Nadia.Agent.Jobs;

[DisallowConcurrentExecution]
public class DnsHealthJob(DnsHealthService dnsService, ILogger<DnsHealthJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try { await dnsService.CheckAllAsync(context.CancellationToken); }
        catch (Exception ex) { logger.LogError(ex, "[Nadia] DnsHealthJob failed"); }
    }
}
