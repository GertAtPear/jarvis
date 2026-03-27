using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace Jarvis.Ui.Auth;

public class JarvisAuthStateProvider(
    ProtectedSessionStorage sessionStorage,
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ILogger<JarvisAuthStateProvider> logger) : AuthenticationStateProvider
{
    private ClaimsPrincipal _anonymous = new(new ClaimsIdentity());

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var tokenResult = await sessionStorage.GetAsync<string>("auth_token");
            if (!tokenResult.Success || string.IsNullOrEmpty(tokenResult.Value))
                return new AuthenticationState(_anonymous);

            var token   = tokenResult.Value;
            var user    = await ValidateTokenAsync(token);
            if (user == null)
            {
                await sessionStorage.DeleteAsync("auth_token");
                return new AuthenticationState(_anonymous);
            }

            var identity = BuildIdentity(user, token);
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Auth state check failed");
            return new AuthenticationState(_anonymous);
        }
    }

    public async Task LoginAsync(string token, UserInfo user)
    {
        await sessionStorage.SetAsync("auth_token", token);
        var identity = BuildIdentity(user, token);
        NotifyAuthenticationStateChanged(
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity))));
    }

    public async Task LogoutAsync()
    {
        await sessionStorage.DeleteAsync("auth_token");
        NotifyAuthenticationStateChanged(
            Task.FromResult(new AuthenticationState(_anonymous)));
    }

    public async Task<string?> GetTokenAsync()
    {
        try
        {
            var result = await sessionStorage.GetAsync<string>("auth_token");
            return result.Success ? result.Value : null;
        }
        catch { return null; }
    }

    private async Task<UserInfo?> ValidateTokenAsync(string token)
    {
        try
        {
            var baseUrl  = config["JarvisApi:BaseUrl"] ?? "http://localhost:5000/";
            var http     = httpFactory.CreateClient();
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var resp     = await http.GetAsync($"{baseUrl.TrimEnd('/')}/api/auth/me");
            if (!resp.IsSuccessStatusCode) return null;

            var me = await resp.Content.ReadFromJsonAsync<MeResponse>();
            return me?.User;
        }
        catch { return null; }
    }

    private static ClaimsIdentity BuildIdentity(UserInfo user, string token)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name,           user.Username),
            new("display_name",            user.DisplayName ?? user.Username),
            new("token",                   token),
        };

        foreach (var role in user.Roles ?? [])
            claims.Add(new Claim(ClaimTypes.Role, role));

        return new ClaimsIdentity(claims, "bearer");
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record UserInfo(
    Guid     Id,
    string   Username,
    string?  DisplayName,
    string[] Roles);

public record MeResponse(UserInfo User, string[] AccessibleAgents);
