using Rocky.Agent.Models;

namespace Rocky.Agent.Services;

/// <summary>
/// Manages per-service Quartz job scheduling at runtime.
/// </summary>
public interface IRockyJobScheduler
{
    Task RefreshJobScheduleAsync(WatchedService service, CancellationToken ct = default);
    Task UnscheduleServiceAsync(Guid serviceId, CancellationToken ct = default);
}
