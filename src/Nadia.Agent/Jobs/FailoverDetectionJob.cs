using Nadia.Agent.Services;
using Quartz;

namespace Nadia.Agent.Jobs;

[DisallowConcurrentExecution]
public class FailoverDetectionJob(FailoverDetectionService failoverService, ILogger<FailoverDetectionJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try { await failoverService.CheckAsync(context.CancellationToken); }
        catch (Exception ex) { logger.LogError(ex, "[Nadia] FailoverDetectionJob failed"); }
    }
}
