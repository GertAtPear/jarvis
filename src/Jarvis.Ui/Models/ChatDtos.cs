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
    DateTimeOffset CreatedAt,
    string? EscalatedFrom = null
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
    bool         SecretPurged = false,
    string?      EscalatedFrom = null
);

public record StatusResponse(
    string Status,
    DateTimeOffset CheckedAt
);

// ── Device DTOs ───────────────────────────────────────────────────────────────

public record DeviceDto(
    Guid            Id,
    string          Name,
    string          DeviceType,
    string?         OsPlatform,
    string?         LahVersion,
    string?         Hostname,
    string?         IpAddress,
    bool            IsOnline,
    string          Status,
    DateTimeOffset? LastSeenAt,
    string?         AdvertisedModulesJson,
    DateTimeOffset  CreatedAt);

public record DevicePermissionDto(
    Guid        Id,
    Guid        DeviceId,
    string      AgentName,
    string      Capability,
    bool        IsGranted,
    bool        RequireConfirm,
    string[]?   PathScope,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record DeviceToolLogDto(
    Guid            Id,
    Guid?           DeviceId,
    string?         AgentName,
    string?         ToolName,
    bool?           Success,
    string?         ErrorMessage,
    int?            DurationMs,
    bool            ConfirmedByUser,
    DateTimeOffset  CreatedAt);

public record RegisterDeviceRequestDto(string Name, string? OsPlatform = null);

public record RegisterDeviceResponseDto(
    Guid    DeviceId,
    string  RegistrationToken,
    DateTimeOffset TokenExpiresAt);

// ── Agent activity feed ───────────────────────────────────────────────────────

public record AgentActivityMessageDto(
    long             Id,
    string           FromAgent,
    string?          ToAgent,
    string           Message,
    long?            ThreadId,
    bool             RequiresApproval,
    DateTimeOffset?  ApprovedAt,
    DateTimeOffset?  DeniedAt,
    DateTimeOffset?  ReadAt,
    DateTimeOffset   CreatedAt,
    string           ApprovalStatus
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
