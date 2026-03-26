namespace Jarvis.Api.Models;

public class SessionSummary
{
    public Guid    Id             { get; init; }
    public string? Title          { get; init; }
    public string? Department     { get; init; }
    public int     MessageCount   { get; init; }
    public DateTimeOffset LastMessageAt { get; init; }
    public DateTimeOffset CreatedAt    { get; init; }
}
