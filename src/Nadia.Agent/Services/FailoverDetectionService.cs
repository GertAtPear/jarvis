using Dapper;
using Nadia.Agent.Data.Repositories;
using Npgsql;

namespace Nadia.Agent.Services;

public class FailoverDetectionService(
    NpgsqlDataSource db,
    FailoverRepository failoverRepo,
    ILogger<FailoverDetectionService> logger)
{
    private string? _lastActiveWan;

    public async Task CheckAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conn = await db.OpenConnectionAsync(ct);

            // Read from Andrew's network_facts — handle gracefully if Andrew hasn't run yet
            string? activeWan;
            try
            {
                activeWan = await conn.ExecuteScalarAsync<string>(
                    "SELECT fact_value FROM andrew_schema.network_facts WHERE fact_key = 'active_wan' LIMIT 1");
            }
            catch
            {
                // andrew_schema may not exist or table may be empty
                return;
            }

            if (activeWan is null) return;

            if (_lastActiveWan is not null && _lastActiveWan != activeWan)
            {
                logger.LogWarning("[Nadia] WAN failover detected: {From} → {To}", _lastActiveWan, activeWan);
                await failoverRepo.InsertAsync(_lastActiveWan, activeWan, "active_wan changed");
            }

            _lastActiveWan = activeWan;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[Nadia] Failover detection check failed");
        }
    }
}
