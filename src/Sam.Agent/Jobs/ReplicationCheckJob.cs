using Quartz;
using Sam.Agent.Data.Repositories;

namespace Sam.Agent.Jobs;

[DisallowConcurrentExecution]
public class ReplicationCheckJob(
    DatabaseRepository databaseRepo,
    ReplicationRepository replRepo,
    ILogger<ReplicationCheckJob> logger) : IJob
{
    private const double LagThresholdSeconds = 30;

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var databases = await databaseRepo.GetAllAsync();

        foreach (var db in databases)
        {
            var status = await replRepo.GetLatestAsync(db.Id);
            if (status is null) continue;

            if (status.ReplicationLagSeconds > LagThresholdSeconds)
            {
                logger.LogWarning("[Sam] Replication lag warning: {Db} lag={Lag:F1}s (threshold={Threshold}s)",
                    db.Name, status.ReplicationLagSeconds, LagThresholdSeconds);
            }
        }
    }
}
