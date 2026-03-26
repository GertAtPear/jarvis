using System.Text.Json.Serialization;

namespace Jarvis.Ui.Models;

public record ChatMessageDto(
    Guid Id,
    string Role,
    string AgentName,
    string Content,
    string? Provider,
    string? Model,
    List<AttachmentDto> Attachments,
    DateTimeOffset CreatedAt
);

public record PendingAttachment(
    string FileName,
    string MimeType,
    string Base64Data,
    long SizeBytes
);

public record AttachmentDto(
    Guid Id,
    string FileName,
    string MimeType,
    string? Base64Preview
);

public record AgentDto(
    string Name,
    string DisplayName,
    string? Department,
    string Status,
    string? LastSeen
);

public record SessionSummaryDto(
    Guid Id,
    string Title,
    DateTimeOffset LastMessageAt,
    int MessageCount
);

public record BriefingResponse(
    string Content,
    DateOnly BriefingDate,
    bool HasItems
);

public record ChatResponse(
    Guid         SessionId,
    string       Response,
    List<string> AgentsUsed,
    int          TotalMs,
    bool         IsMorningBriefing,
    bool         SecretPurged = false
);

public record StatusResponse(
    string Status,
    DateTimeOffset CheckedAt
);

/// <summary>File data passed from JavaScript paste/drop handlers.</summary>
public class FilePayload
{
    [JsonPropertyName("base64")]
    public string Base64 { get; set; } = "";

    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }
}
