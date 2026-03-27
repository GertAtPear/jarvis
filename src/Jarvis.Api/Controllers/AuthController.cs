using Mediahost.Auth.Models;
using Mediahost.Auth.Services;
using Microsoft.AspNetCore.Mvc;

namespace Jarvis.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AuthService auth) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> LoginAsync([FromBody] LoginRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { error = "Username and password are required." });

        var result = await auth.LoginAsync(req.Username, req.Password, ct);
        if (result == null)
            return Unauthorized(new { error = "Invalid username or password." });

        return Ok(result);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> LogoutAsync(CancellationToken ct)
    {
        var token = HttpContext.Items["Token"] as string;
        if (token != null)
            await auth.LogoutAsync(token, ct);
        return Ok(new { success = true });
    }

    [HttpGet("me")]
    public async Task<IActionResult> MeAsync(CancellationToken ct)
    {
        var user = HttpContext.Items["User"] as UserRecord;
        if (user == null)
            return Unauthorized(new { error = "Not authenticated." });

        var userRepo         = HttpContext.RequestServices.GetRequiredService<UserRepository>();
        var accessibleAgents = await userRepo.GetAccessibleAgentsAsync(user.Id);

        return Ok(new { user, accessible_agents = accessibleAgents });
    }
}
