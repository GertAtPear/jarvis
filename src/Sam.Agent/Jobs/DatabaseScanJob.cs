using Quartz;
using Sam.Agent.Data.Repositories;
using Sam.Agent.Services;

namespace Sam.Agent.Jobs;

[DisallowConcurrentExecution]
public class DatabaseScanJob(
    DatabaseRepository databaseRepo,
    MySqlScanService mySqlScan,
    PostgreSqlScanService pgScan,
    ILogger<DatabaseScanJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var databases = await databaseRepo.GetAllAsync();

        foreach (var db in databases)
        {
            try
            {
                if (db.DbType.Equals("postgresql", StringComparison.OrdinalIgnoreCase))
                    await pgScan.ScanAsync(db, ct);
                else
                    await mySqlScan.ScanAsync(db, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Sam] Scan job failed for {Db}", db.Name);
            }
        }
    }
}
