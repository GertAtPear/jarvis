using Lexi.Agent.Services;
using Quartz;

namespace Lexi.Agent.Jobs;

[DisallowConcurrentExecution]
public class AccessLogJob(AccessLogAnalyserService analyser, ILogger<AccessLogJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try { await analyser.AnalyseAsync(context.CancellationToken); }
        catch (Exception ex) { logger.LogError(ex, "[Lexi] AccessLogJob failed"); }
    }
}
