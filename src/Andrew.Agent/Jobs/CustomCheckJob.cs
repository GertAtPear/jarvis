using System.Diagnostics;
using System.Text.Json;
using Andrew.Agent.Data.Repositories;
using Andrew.Agent.Jobs.CheckExecutors;
using Quartz;

namespace Andrew.Agent.Jobs;

/// <summary>
/// Dispatches a user-defined scheduled check by check type.
/// Each check has its own Quartz job instance keyed by check ID.
/// </summary>
[DisallowConcurrentExecution]
public class CustomCheckJob(
    ScheduledCheckRepository checkRepo,
    ContainerCheckExecutor containerCheck,
    ServerUpCheckExecutor serverUpCheck,
    WebsiteUpCheckExecutor websiteCheck,
    ILogger<CustomCheckJob> logger) : IJob
{
    /// <summary>JobDataMap key holding the check ID (UUID string).</summary>
    public const string CheckIdKey = "check_id";

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var checkIdStr = context.JobDetail.JobDataMap.GetString(CheckIdKey);

        if (!Guid.TryParse(checkIdStr, out var checkId))
        {
            logger.LogError("CustomCheckJob fired with missing/invalid check_id in job data");
            return;
        }

        var check = await checkRepo.GetByIdAsync(checkId);
        if (check is null)
        {
            logger.LogWarning("CustomCheckJob: check {Id} not found — job will be removed", checkId);
            await context.Scheduler.DeleteJob(context.JobDetail.Key, ct);
            return;
        }

        if (!check.IsActive)
        {
            logger.LogDebug("CustomCheckJob: check '{Name}' is inactive, skipping", check.Name);
            return;
        }

        var sw = Stopwatch.StartNew();
        string status;
        string? detailsJson;

        try
        {
            var (ok, details) = check.CheckType switch
            {
                "container_running" => await containerCheck.ExecuteAsync(check, ct),
                "server_up"         => await serverUpCheck.ExecuteAsync(check, ct),
                "website_up"        => await websiteCheck.ExecuteAsync(check, ct),
                "port_listening"    => await serverUpCheck.ExecutePortAsync(check, ct),
                _ => (false, $"Unknown check type: '{check.CheckType}'")
            };

            sw.Stop();
            status = ok ? "ok" : "failed";
            detailsJson = JsonSerializer.Serialize(
                new { result = details, check_name = check.Name, check_type = check.CheckType },
                JsonOpts);
        }
        catch (Exception ex)
        {
            sw.Stop();
            status = "failed";
            detailsJson = JsonSerializer.Serialize(
                new { error = ex.Message, check_name = check.Name },
                JsonOpts);
            logger.LogError(ex, "Unhandled error in check '{Name}'", check.Name);
        }

        await checkRepo.UpdateLastResultAsync(checkId, status, detailsJson);
        await checkRepo.LogResultAsync(checkId, status, detailsJson, (int)sw.ElapsedMilliseconds);

        if (status == "failed" && check.NotifyOnFailure)
            logger.LogWarning("CHECK FAILED ❌ [{Name}] {Type} → {Target}",
                check.Name, check.CheckType, check.Target);
        else
            logger.LogDebug("CHECK OK ✅ [{Name}] {DurationMs}ms", check.Name, sw.ElapsedMilliseconds);
    }
}
