using Mediahost.Shared.Services;
using Microsoft.Extensions.Logging;

namespace Mediahost.Vault.Services;

/// <summary>
/// Wraps IVaultService and enforces per-agent path-based access control.
/// An agent may only access paths that start with one of its owned prefixes.
/// Cross-agent grants are checked via the optional grantChecker delegate
/// (supplied at DI registration; backed by jarvis_schema.vault_grants).
/// </summary>
public sealed class ScopedVaultService(
    IVaultService inner,
    string agentName,
    IReadOnlyList<string> ownedPrefixes,
    ILogger<ScopedVaultService> logger,
    Func<string, string, CancellationToken, Task<bool>>? grantChecker = null)
    : IScopedVaultService
{
    public string AgentName => agentName;

    public async Task<string?> GetSecretAsync(string path, string key, CancellationToken ct = default)
    {
        await AssertAccessAsync(path, write: false, ct);
        return await inner.GetSecretAsync(path, key, ct);
    }

    public async Task<Dictionary<string, string>> GetSecretsBulkAsync(string path, CancellationToken ct = default)
    {
        await AssertAccessAsync(path, write: false, ct);
        return await inner.GetSecretsBulkAsync(path, ct);
    }

    public async Task SetSecretAsync(string path, string key, string value, CancellationToken ct = default)
    {
        await AssertAccessAsync(path, write: true, ct);
        await inner.SetSecretAsync(path, key, value, ct);
    }

    public async Task<bool> SecretExistsAsync(string path, string key, CancellationToken ct = default)
    {
        await AssertAccessAsync(path, write: false, ct);
        return await inner.SecretExistsAsync(path, key, ct);
    }

    // ── Access enforcement ─────────────────────────────────────────────────

    private async Task AssertAccessAsync(string path, bool write, CancellationToken ct)
    {
        var normalized = NormalizePath(path);

        if (IsOwned(normalized)) return;

        // For read access, check the grants table if a checker was provided
        if (!write && grantChecker is not null)
        {
            var granted = await grantChecker(agentName, normalized, ct);
            if (granted) return;
        }

        var op = write ? "write" : "read";
        logger.LogWarning(
            "[VaultScope] Agent '{Agent}' denied {Op} access to '{Path}'. Owned: [{Prefixes}]",
            agentName, op, path, string.Join(", ", ownedPrefixes));

        throw new VaultAccessDeniedException(agentName, path, op);
    }

    private bool IsOwned(string normalizedPath) =>
        ownedPrefixes.Any(p =>
            normalizedPath.StartsWith(NormalizePath(p), StringComparison.OrdinalIgnoreCase));

    private static string NormalizePath(string path) =>
        "/" + path.TrimStart('/').ToLowerInvariant();
}

/// <summary>Thrown when an agent attempts to access a vault path outside its scope.</summary>
public sealed class VaultAccessDeniedException(string agentName, string path, string operation)
    : UnauthorizedAccessException(
        $"Agent '{agentName}' is not allowed to {operation} vault path '{path}'.");
