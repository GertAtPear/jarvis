using Jarvis.Api.Data;
using Jarvis.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Jarvis.Api.Services;

/// <summary>
/// Background service that polls jarvis_schema.agent_messages every 5 seconds
/// and pushes new messages to the UI via SignalR AgentActivityHub.
///
/// Pushes two event types:
///   - "AgentMessage"   → every new message (agent activity feed)
///   - "ApprovalNeeded" → messages with requires_approval=true awaiting decision
/// </summary>
public class AgentMessagePollingService(
    IServiceScopeFactory scopeFactory,
    IHubContext<AgentActivityHub> hub,
    ILogger<AgentMessagePollingService> logger) : BackgroundService
{
    private long _lastSeenId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the app time to fully start before we start polling
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        _lastSeenId = await GetMaxIdAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "[AgentMessagePolling] Poll cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<AgentMessageRepository>();

        var newMessages = await repo.GetSinceAsync(_lastSeenId, ct);
        if (newMessages.Count == 0) return;

        foreach (var msg in newMessages)
        {
            // Push every message to the activity feed
            await hub.Clients.All.SendAsync("AgentMessage", msg, ct);

            // Also push approval-needed events separately so the UI can highlight them
            if (msg.RequiresApproval && msg.ApprovedAt is null && msg.DeniedAt is null)
                await hub.Clients.All.SendAsync("ApprovalNeeded", msg, ct);

            if (msg.Id > _lastSeenId)
                _lastSeenId = msg.Id;
        }

        logger.LogDebug("[AgentMessagePolling] Pushed {Count} new agent message(s)", newMessages.Count);
    }

    private async Task<long> GetMaxIdAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<AgentMessageRepository>();
            return await repo.GetMaxIdAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[AgentMessagePolling] Could not load initial max ID — starting from 0");
            return 0;
        }
    }
}
