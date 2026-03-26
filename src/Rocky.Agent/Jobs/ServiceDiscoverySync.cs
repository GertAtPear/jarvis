using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using Rocky.Agent.Data;
using Rocky.Agent.Data.Repositories;
using Rocky.Agent.Models;
using Rocky.Agent.Services;

namespace Rocky.Agent.Jobs;

/// <summary>
/// Syncs watched services from andrew_schema.containers to rocky_schema.watched_services
/// every 30 minutes. Only auto-discovers container_running checks — manual services are untouched.
/// </summary>
[DisallowConcurrentExecution]
public class ServiceDiscoverySync(IServiceScopeFactory scopeFactory, ILogger<ServiceDiscoverySync> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db          = scope.ServiceProvider.GetRequiredService<DbConnectionFactory>();
        var serviceRepo = scope.ServiceProvider.GetRequiredService<WatchedServiceRepository>();
        var jobScheduler = scope.ServiceProvider.GetRequiredService<IRockyJobScheduler>();

        try
        {
            await using var conn = db.Create();

            // Read containers from andrew_schema (read-only cross-schema access)
            var containers = (await conn.QueryAsync<ContainerRow>("""
                SELECT
                    s.hostname  AS ServerHostname,
                    c.name      AS ContainerName,
                    c.image     AS Image,
                    c.status    AS Status
                FROM andrew_schema.containers c
                JOIN andrew_schema.servers    s ON s.id = c.server_id
                WHERE c.status = 'running'
                ORDER BY s.hostname, c.name
                """)).ToList();

            if (containers.Count == 0)
            {
                logger.LogDebug("[Rocky] ServiceDiscoverySync: no running containers found in andrew_schema");
                return;
            }

            var existingServices = (await serviceRepo.GetAllAsync()).ToList();

            foreach (var container in containers)
            {
                var serviceName = $"auto.container.{container.ServerHostname}.{container.ContainerName}"
                    .ToLowerInvariant().Replace(' ', '-');

                // Only insert if not already tracked
                var exists = existingServices.Any(s =>
                    s.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase));

                if (!exists)
                {
                    var checkConfig = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        server    = container.ServerHostname,
                        container = container.ContainerName
                    });

                    await conn.ExecuteAsync("""
                        INSERT INTO rocky_schema.watched_services
                            (name, display_name, check_type, check_config, interval_seconds, enabled)
                        VALUES
                            (@name, @displayName, 'container_running', @checkConfig::jsonb, 60, true)
                        ON CONFLICT (name) DO NOTHING
                        """, new
                    {
                        name        = serviceName,
                        displayName = $"{container.ContainerName} ({container.ServerHostname})",
                        checkConfig
                    });

                    // Schedule the newly-discovered service
                    var newService = await serviceRepo.GetByNameAsync(serviceName);
                    if (newService is not null)
                        await jobScheduler.RefreshJobScheduleAsync(newService, context.CancellationToken);

                    logger.LogInformation("[Rocky] Auto-discovered container service: {Name}", serviceName);
                }
            }

            logger.LogDebug("[Rocky] ServiceDiscoverySync complete — {Count} containers scanned", containers.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Rocky] ServiceDiscoverySync failed");
        }
    }

    private sealed record ContainerRow(string ServerHostname, string ContainerName, string Image, string Status);
}
