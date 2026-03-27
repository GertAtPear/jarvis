namespace Mediahost.Auth.Models;

public record TokenResponse(
    string         Token,
    DateTimeOffset ExpiresAt,
    UserRecord     User);
