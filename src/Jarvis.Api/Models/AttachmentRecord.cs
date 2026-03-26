namespace Jarvis.Api.Models;

public class AttachmentRecord
{
    public Guid    Id              { get; init; }
    public Guid?   ConversationId  { get; init; }
    public string  FileName        { get; init; } = "";
    public string  MimeType        { get; init; } = "";
    public long    FileSizeBytes   { get; init; }
    public string? StoragePath     { get; init; }
    public string? Base64Preview   { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
