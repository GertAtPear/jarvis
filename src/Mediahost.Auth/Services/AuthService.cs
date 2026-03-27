using System.Security.Cryptography;
using Mediahost.Auth.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Mediahost.Auth.Services;

public class AuthService(
    UserRepository     userRepo,
    PasswordHasher     hasher,
    IConfiguration     config,
    ILogger<AuthService> logger)
{
    private int TokenExpiryHours =>
        int.TryParse(config["Auth:TokenExpiryHours"], out var h) ? h : 12;

    public async Task<TokenResponse?> LoginAsync(
        string username, string password, CancellationToken ct = default)
    {
        var hash = await userRepo.GetPasswordHashAsync(username);
        if (hash == null)
        {
            // Dummy compare to prevent timing oracle
            hasher.Verify("dummy", "$2a$12$dummyhashfortimingprotection00000000000000000000000");
            logger.LogWarning("Login failed: user '{Username}' not found", username);
            return null;
        }

        if (!hasher.Verify(password, hash))
        {
            logger.LogWarning("Login failed: wrong password for '{Username}'", username);
            return null;
        }

        var user      = await userRepo.GetByUsernameAsync(username);
        if (user == null) return null;

        // Generate 256-bit random token
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token      = Convert.ToHexString(tokenBytes).ToLower();
        var tokenHash  = UserRepository.HashToken(token);
        var expiresAt  = DateTimeOffset.UtcNow.AddHours(TokenExpiryHours);

        await userRepo.StoreSessionAsync(user.Id, tokenHash, expiresAt);
        await userRepo.UpdateLastLoginAsync(user.Id);

        logger.LogInformation("User '{Username}' logged in", username);
        return new TokenResponse(token, expiresAt, user);
    }

    public Task<UserRecord?> ValidateTokenAsync(string token, CancellationToken ct = default) =>
        userRepo.ValidateTokenAsync(token);

    public Task LogoutAsync(string token, CancellationToken ct = default) =>
        userRepo.DeleteSessionAsync(token);
}
