using System.Text.Json;

namespace Jarvis.Api.Models;

public record DeviceRecord(
    Guid                Id,
    string              Name,
    string              DeviceType,
    string?             OsPlatform,
    string?             RegistrationToken,
    DateTimeOffset?     TokenExpiresAt,
    string?             DeviceTokenPath,
    string              Status,
    DateTimeOffset?     LastSeenAt,
    string?             Hostname,
    string?             IpAddress,
    string?             LahVersion,
    string?             AdvertisedModulesJson,
    DateTimeOffset      CreatedAt)
{
    public bool IsOnline => Status == "online";
}

public record DevicePermissionRecord(
    Guid        Id,
    Guid        DeviceId,
    string      AgentName,
    string      Capability,
    bool        IsGranted,
    bool        RequireConfirm,
    string[]?   PathScope,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record DeviceToolLogEntry(
    Guid            Id,
    Guid?           DeviceId,
    string?         AgentName,
    string?         ToolName,
    string?         ParametersJson,
    bool?           Success,
    string?         ErrorMessage,
    int?            DurationMs,
    bool            ConfirmedByUser,
    DateTimeOffset  CreatedAt);

// ── Request/response DTOs ─────────────────────────────────────────────────────

public record RegisterDeviceRequest(string Name, string? OsPlatform = null);

public record RegisterDeviceResponse(
    Guid    DeviceId,
    string  RegistrationToken,
    DateTimeOffset TokenExpiresAt);

public record UpdatePermissionsRequest(
    List<PermissionUpdate> Permissions);

public record PermissionUpdate(
    string      AgentName,
    string      Capability,
    bool        IsGranted,
    bool        RequireConfirm,
    string[]?   PathScope);

public record DeviceToolExecuteRequest(
    string              ToolName,
    JsonElement         Parameters,
    bool                RequireConfirm = false);

// Used internally to parse advertised_modules JSON from LAH
internal record AdvertisedModule(string Module, List<string> Tools);
