namespace Eve.Agent.Models;

public class Contact
{
    public Guid      Id              { get; init; }
    public string    Name            { get; init; } = "";
    public string?   Relationship    { get; init; }   // e.g. "girlfriend", "sister", "colleague"
    public string?   ContactType     { get; init; }   // "person" | "business"
    public string?   Company         { get; init; }
    public string?   PhoneCell       { get; init; }
    public string?   PhoneWork       { get; init; }
    public string?   PhoneHome       { get; init; }
    public string?   EmailPersonal   { get; init; }
    public string?   EmailWork       { get; init; }
    public string?   AddressHome     { get; init; }
    public string?   AddressWork     { get; init; }
    public string?   Website         { get; init; }
    public string?   SocialLinksJson { get; init; }   // JSONB: { "facebook": "...", "linkedin": "..." }
    public string?   ExtraJson       { get; init; }   // JSONB: overflow / future fields
    public DateOnly? Birthday        { get; init; }
    public DateOnly? Anniversary     { get; init; }
    public string?   Notes           { get; init; }
    public string?   TagsJson        { get; init; }
    public DateTimeOffset CreatedAt  { get; init; }
}
