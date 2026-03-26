namespace Jarvis.Api.Models;

public class AgentMessage
{
    public Guid    Id         { get; init; }
    public Guid    SessionId  { get; init; }
    public string  Role       { get; init; } = "";   // user|assistant|agent|tool
    public string? AgentName  { get; init; }
    public string  Content    { get; init; } = "";
    public string? ToolCallsJson { get; init; }
    public string? Department { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
