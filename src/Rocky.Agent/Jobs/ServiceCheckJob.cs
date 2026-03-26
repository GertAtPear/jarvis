using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using Rocky.Agent.Data.Repositories;
using Rocky.Agent.Models;
using Rocky.Agent.Services;

namespace Rocky.Agent.Jobs;

/// <summary>
/// Per-service Quartz job that runs the appropriate health check and persists the result.
/// One job instance per watched service, keyed by service ID.
/// </summary>
[DisallowConcurrentExecution]
public class ServiceCheckJob(IServiceScopeFactory scopeFactory, ILogger<ServiceCheckJob> logger) : IJob
{
    public const string ServiceIdKey   = "service_id";
    public const string ServiceNameKey = "service_name";

    public async Task Execute(IJobExecutionContext context)
    {
        var dataMap   = context.JobDetail.JobDataMap;
        var serviceId = Guid.Parse(dataMap.GetString(ServiceIdKey)!);
        var name      = dataMap.GetString(ServiceNameKey) ?? serviceId.ToString();

        await using var scope = scopeFactory.CreateAsyncScope();
        var executor    = scope.ServiceProvider.GetRequiredService<CheckExecutorService>();
        var checkRepo   = scope.ServiceProvider.GetRequiredService<CheckResultRepository>();
        var alertRepo   = scope.ServiceProvider.GetRequiredService<AlertRepository>();
        var serviceRepo = scope.ServiceProvider.GetRequiredService<WatchedServiceRepository>();

        WatchedService? service;
        try
        {
            service = await serviceRepo.GetByIdAsync(serviceId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Rocky] ServiceCheckJob: could not load service {Id}", serviceId);
            return;
        }

        if (service is null || !service.Enabled)
        {
            logger.LogDebug("[Rocky] ServiceCheckJob: service {Id} not found or disabled — skipping", serviceId);
            return;
        }

        CheckResult result;
        try
        {
            result = await executor.ExecuteAsync(service, context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Rocky] Check executor threw for service '{Name}'", name);
            result = new CheckResult
            {
                Id        = Guid.NewGuid(),
                ServiceId = serviceId,
                IsHealthy = false,
                Detail    = $"Unhandled error: {ex.Message}",
                DurationMs = 0,
                CheckedAt = DateTime.UtcNow
            };
        }

        try { await checkRepo.InsertAsync(result); }
        catch (Exception ex) { logger.LogError(ex, "[Rocky] Failed to persist check result for '{Name}'", name); }

        // ── Alert logic ───────────────────────────────────────────────────────
        try
        {
            if (!result.IsHealthy)
            {
                var alreadyAlerting = await alertRepo.HasUnresolvedAsync(serviceId);
                if (!alreadyAlerting)
                {
                    var alert = new AlertRecord
                    {
                        ServiceId = serviceId,
                        Severity  = "warning",
                        Message   = $"Service '{service.DisplayName}' failed check: {result.Detail}",
                        Resolved  = false
                    };
                    await alertRepo.InsertAsync(alert);
                    logger.LogWarning("[Rocky] Alert raised for service '{Name}': {Detail}", name, result.Detail);
                }
            }
            else
            {
                // Resolve any open alerts
                await alertRepo.ResolveAsync(serviceId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Rocky] Alert logic failed for service '{Name}'", name);
        }

        logger.LogDebug("[Rocky] Check '{Name}': {Status} in {Ms}ms — {Detail}",
            name, result.IsHealthy ? "healthy" : "UNHEALTHY", result.DurationMs, result.Detail);
    }
}
