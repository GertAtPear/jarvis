using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using Rocky.Agent.Data.Repositories;

namespace Rocky.Agent.Jobs;

/// <summary>
/// Purges check_results older than 48 hours. Runs daily at 03:00 SAST (01:00 UTC).
/// </summary>
[DisallowConcurrentExecution]
public class ResultsCleanupJob(IServiceScopeFactory scopeFactory, ILogger<ResultsCleanupJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var checkRepo = scope.ServiceProvider.GetRequiredService<CheckResultRepository>();

        var cutoff = DateTime.UtcNow.AddHours(-48);
        try
        {
            await checkRepo.DeleteOlderThanAsync(cutoff);
            logger.LogInformation("[Rocky] Results cleanup: purged check_results older than {Cutoff:u}", cutoff);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Rocky] Results cleanup failed");
        }
    }
}
