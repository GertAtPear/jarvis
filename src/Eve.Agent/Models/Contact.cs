namespace Eve.Agent.Models;

public class Contact
{
    public Guid      Id           { get; init; }
    public string    Name         { get; init; } = "";
    public string?   Relationship { get; init; }
    public DateOnly? Birthday     { get; init; }
    public DateOnly? Anniversary  { get; init; }
    public string?   Notes        { get; init; }
    public string?   TagsJson     { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
