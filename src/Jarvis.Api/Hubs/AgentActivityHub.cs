using Microsoft.AspNetCore.SignalR;

namespace Jarvis.Api.Hubs;

/// <summary>
/// SignalR hub for real-time agent activity feed.
/// The UI connects to /hubs/chat and receives:
///   - "AgentMessage"   → new inter-agent message (AgentActivityMessage)
///   - "ApprovalNeeded" → a message requiring Gert's approval
///
/// The background AgentMessagePollingService pushes events here.
/// Clients call approve/deny via the REST API (AgentMessagesController).
/// </summary>
public class AgentActivityHub : Hub
{
    // Hub is intentionally thin — events are pushed from AgentMessagePollingService
}
