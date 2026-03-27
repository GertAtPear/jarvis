using Mediahost.Auth.Models;
using Mediahost.Auth.Services;
using Microsoft.AspNetCore.Mvc;

namespace Jarvis.Api.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController(
    UserRepository userRepo,
    PasswordHasher hasher) : ControllerBase
{
    private IActionResult RequireAdmin()
    {
        var user = HttpContext.Items["User"] as UserRecord;
        if (user == null) return Unauthorized(new { error = "Not authenticated." });
        if (!user.Roles.Contains("admin")) return Forbid();
        return null!;
    }

    [HttpGet("users")]
    public async Task<IActionResult> ListUsersAsync()
    {
        var check = RequireAdmin();
        if (check != null) return check;

        var users = await userRepo.ListAllUsersAsync();
        return Ok(users);
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUserAsync(
        [FromBody] CreateUserRequest req, CancellationToken ct)
    {
        var check = RequireAdmin();
        if (check != null) return check;

        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { error = "Username and password are required." });

        var passwordHash = hasher.Hash(req.Password);
        var userId       = await userRepo.CreateUserAsync(
            req.Username, req.DisplayName ?? req.Username, req.Email, passwordHash);

        if (req.Roles?.Length > 0)
            await userRepo.SetUserRolesAsync(userId, req.Roles);

        return Ok(new { id = userId, username = req.Username, message = "User created." });
    }

    [HttpPut("users/{id:guid}")]
    public async Task<IActionResult> UpdateUserAsync(
        Guid id, [FromBody] UpdateUserRequest req, CancellationToken ct)
    {
        var check = RequireAdmin();
        if (check != null) return check;

        await userRepo.UpdateUserAsync(id, req.DisplayName, req.Email, req.IsActive);
        return Ok(new { id, message = "User updated." });
    }

    [HttpPut("users/{id:guid}/password")]
    public async Task<IActionResult> SetPasswordAsync(
        Guid id, [FromBody] SetPasswordRequest req, CancellationToken ct)
    {
        var check = RequireAdmin();
        if (check != null) return check;

        if (string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { error = "Password is required." });

        await userRepo.UpdatePasswordAsync(id, hasher.Hash(req.Password));
        return Ok(new { id, message = "Password updated." });
    }

    [HttpPut("users/{id:guid}/roles")]
    public async Task<IActionResult> SetRolesAsync(
        Guid id, [FromBody] SetRolesRequest req, CancellationToken ct)
    {
        var check = RequireAdmin();
        if (check != null) return check;

        await userRepo.SetUserRolesAsync(id, req.Roles ?? []);
        return Ok(new { id, roles = req.Roles, message = "Roles updated." });
    }
}

// ── Request models ─────────────────────────────────────────────────────────────

public record CreateUserRequest(
    string    Username,
    string?   DisplayName,
    string?   Email,
    string    Password,
    string[]? Roles);

public record UpdateUserRequest(
    string?  DisplayName,
    string?  Email,
    bool?    IsActive);

public record SetPasswordRequest(string Password);

public record SetRolesRequest(string[]? Roles);
