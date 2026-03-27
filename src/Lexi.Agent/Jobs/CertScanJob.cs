using Dapper;
using Lexi.Agent.Services;
using Npgsql;
using Quartz;

namespace Lexi.Agent.Jobs;

[DisallowConcurrentExecution]
public class CertScanJob(
    CertCheckService certService,
    NpgsqlDataSource db,
    ILogger<CertScanJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        try
        {
            // Get server hostnames from Andrew's registry
            List<string> hosts;
            try
            {
                await using var conn = await db.OpenConnectionAsync(ct);
                hosts = (await conn.QueryAsync<string>(
                    "SELECT hostname FROM andrew_schema.servers WHERE is_active = true")).ToList();
            }
            catch
            {
                hosts = [];
            }

            foreach (var host in hosts)
            {
                try { await certService.CheckSingleAsync(host, 443, ct); }
                catch (Exception ex) { logger.LogDebug(ex, "[Lexi] Cert check failed for {Host}", host); }
            }
        }
        catch (Exception ex) { logger.LogError(ex, "[Lexi] CertScanJob failed"); }
    }
}
