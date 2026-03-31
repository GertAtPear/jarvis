namespace Jarvis.Api.Models;

/// <summary>
/// Represents an inter-agent message from the agent message bus.
/// Distinct from AgentMessage (which is a conversation turn in a session).
/// </summary>
public class AgentActivityMessage
{
    public long              Id                { get; init; }
    public string            FromAgent         { get; init; } = "";
    public string?           ToAgent           { get; init; }
    public string            Message           { get; init; } = "";
    public long?             ThreadId          { get; init; }
    public bool              RequiresApproval  { get; init; }
    public DateTimeOffset?   ApprovedAt        { get; init; }
    public DateTimeOffset?   DeniedAt          { get; init; }
    public DateTimeOffset?   ReadAt            { get; init; }
    public DateTimeOffset    CreatedAt         { get; init; }

    public string ApprovalStatus => RequiresApproval
        ? ApprovedAt.HasValue ? "approved"
        : DeniedAt.HasValue   ? "denied"
        : "pending"
        : "n/a";
}
