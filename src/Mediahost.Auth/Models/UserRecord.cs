namespace Mediahost.Auth.Models;

public record UserRecord(
    Guid                   Id,
    string                 Username,
    string                 DisplayName,
    string?                Email,
    bool                   IsActive,
    DateTimeOffset?        LastLoginAt,
    DateTimeOffset         CreatedAt,
    IReadOnlyList<string>  Roles);
