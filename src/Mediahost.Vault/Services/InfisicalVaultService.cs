// IMPORTANT: This service must NEVER log secret values.
// Only log paths and keys — never values. Violating this rule risks leaking
// credentials into log aggregators (Loki, Seq, CloudWatch, etc.).

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Mediahost.Shared.Services;
using Mediahost.Vault.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Mediahost.Vault.Services;

public sealed class InfisicalVaultService : IVaultService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<InfisicalVaultService> _logger;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _projectId;
    private readonly string _environment;

    private string? _accessToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public InfisicalVaultService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<InfisicalVaultService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _clientId = configuration["Infisical:ClientId"]
            ?? throw new InvalidOperationException("Infisical:ClientId is required");
        _clientSecret = configuration["Infisical:ClientSecret"]
            ?? throw new InvalidOperationException("Infisical:ClientSecret is required");
        _projectId = configuration["Infisical:ProjectId"]
            ?? throw new InvalidOperationException("Infisical:ProjectId is required");
        _environment = configuration["Infisical:Environment"] ?? "prod";
    }

    // -------------------------------------------------------------------------
    // Token management
    // -------------------------------------------------------------------------

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        // Fast path — token still valid
        if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiry.AddSeconds(-60))
            return _accessToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring the lock
            if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiry.AddSeconds(-60))
                return _accessToken;

            _logger.LogDebug("Refreshing Infisical access token");

            var client = _httpClientFactory.CreateClient("infisical");
            var response = await client.PostAsJsonAsync(
                "/api/v1/auth/universal-auth/login",
                new { clientId = _clientId, clientSecret = _clientSecret },
                ct);

            if (!response.IsSuccessStatusCode)
                throw new InfisicalException(
                    $"Authentication failed: {response.StatusCode}",
                    (int)response.StatusCode);

            var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(ct)
                ?? throw new InfisicalException("Empty authentication response", (int)response.StatusCode);

            _accessToken = auth.AccessToken;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(auth.ExpiresIn);
            _logger.LogDebug("Infisical token refreshed, expires at {Expiry:O}", _tokenExpiry);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<HttpClient> GetAuthorizedClientAsync(CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);
        var client = _httpClientFactory.CreateClient("infisical");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // -------------------------------------------------------------------------
    // IVaultService implementation
    // -------------------------------------------------------------------------

    public async Task<string?> GetSecretAsync(string path, string key, CancellationToken ct = default)
    {
        _logger.LogDebug("GetSecret path={Path} key={Key}", path, key);

        var client = await GetAuthorizedClientAsync(ct);
        var url = $"/api/v3/secrets/raw/{Uri.EscapeDataString(key)}"
            + $"?workspaceId={Uri.EscapeDataString(_projectId)}"
            + $"&environment={Uri.EscapeDataString(_environment)}"
            + $"&secretPath={EncodePath(path)}";

        var response = await client.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
            throw new InfisicalException(
                $"Failed to get secret path={path} key={key}: {response.StatusCode}",
                (int)response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<SingleSecretResponse>(ct);
        return result?.Secret?.SecretValue;
    }

    public async Task<Dictionary<string, string>> GetSecretsBulkAsync(string path, CancellationToken ct = default)
    {
        _logger.LogDebug("GetSecretsBulk path={Path}", path);

        var client = await GetAuthorizedClientAsync(ct);
        var url = $"/api/v3/secrets/raw"
            + $"?workspaceId={Uri.EscapeDataString(_projectId)}"
            + $"&environment={Uri.EscapeDataString(_environment)}"
            + $"&secretPath={EncodePath(path)}";

        var response = await client.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
            throw new InfisicalException(
                $"Failed to get secrets at path={path}: {response.StatusCode}",
                (int)response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<BulkSecretsResponse>(ct);
        return result?.Secrets?
            .ToDictionary(s => s.SecretKey, s => s.SecretValue ?? string.Empty)
            ?? [];
    }

    public async Task SetSecretAsync(string path, string key, string value, CancellationToken ct = default)
    {
        _logger.LogDebug("SetSecret path={Path} key={Key}", path, key);
        // NOTE: value is intentionally excluded from the log statement above.

        // Normalize: Infisical requires absolute paths starting with /
        var normalizedPath = "/" + path.TrimStart('/');

        await EnsureFolderPathAsync(normalizedPath, ct);

        var client = await GetAuthorizedClientAsync(ct);
        var url = $"/api/v3/secrets/raw/{Uri.EscapeDataString(key)}";
        var payload = new
        {
            workspaceId = _projectId,
            environment = _environment,
            secretPath = normalizedPath,
            secretValue = value,
            type = "shared"
        };

        // Try create (POST) first; if the secret already exists Infisical returns 400,
        // so fall back to update (PATCH).
        var response = await client.PostAsJsonAsync(url, payload, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            _logger.LogDebug("SetSecret POST returned 400 — retrying as PATCH (secret may already exist)");
            response = await client.PatchAsJsonAsync(url, payload, ct);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InfisicalException(
                $"Failed to set secret path={normalizedPath} key={key}: {response.StatusCode} — {body}",
                (int)response.StatusCode);
        }
    }

    private async Task EnsureFolderPathAsync(string path, CancellationToken ct)
    {
        // Build each ancestor path segment and create folders as needed.
        // E.g. "/servers/172.31.14.230" → create "/servers", then "/servers/172.31.14.230"
        var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return;

        var client = await GetAuthorizedClientAsync(ct);

        var current = string.Empty; // parent starts as root
        foreach (var segment in segments)
        {
            var parentPath = string.IsNullOrEmpty(current) ? "/" : $"/{current}";
            var response = await client.PostAsJsonAsync("/api/v1/folders", new
            {
                workspaceId = _projectId,
                environment = _environment,
                path = parentPath,
                name = segment
            }, ct);

            // 200/201 = created; 400 with "already exists" is fine to ignore
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                // Infisical returns 400 when the folder already exists — that's acceptable
                if (response.StatusCode != System.Net.HttpStatusCode.BadRequest ||
                    !body.Contains("already exist", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Failed to create folder '{Parent}/{Segment}': {Status} {Body}",
                        parentPath, segment, response.StatusCode, body);
                    // Non-fatal — continue and let the secret write fail with a clearer error if the path is truly wrong
                }
            }

            current = string.IsNullOrEmpty(current) ? segment : $"{current}/{segment}";
        }
    }

    public async Task<bool> SecretExistsAsync(string path, string key, CancellationToken ct = default)
        => await GetSecretAsync(path, key, ct) is not null;

    // Encode each path segment but preserve '/' separators, as Infisical uses
    // them for folder routing and does not accept %2F in the secretPath param.
    private static string EncodePath(string path) =>
        string.Join("/", path.Split('/').Select(Uri.EscapeDataString));

    // -------------------------------------------------------------------------
    // Private response DTOs
    // -------------------------------------------------------------------------

    private sealed record AuthResponse(
        [property: JsonPropertyName("accessToken")] string AccessToken,
        [property: JsonPropertyName("expiresIn")] int ExpiresIn);

    private sealed record SingleSecretResponse(
        [property: JsonPropertyName("secret")] SecretData? Secret);

    private sealed record SecretData(
        [property: JsonPropertyName("secretKey")] string SecretKey,
        [property: JsonPropertyName("secretValue")] string? SecretValue);

    private sealed record BulkSecretsResponse(
        [property: JsonPropertyName("secrets")] List<SecretData>? Secrets);
}
