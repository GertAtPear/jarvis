namespace Mediahost.Shared.Services;

/// <summary>
/// A vault service scoped to a specific agent. Enforces path-based access control:
/// the agent can only read/write its owned path prefixes, or paths that have been
/// explicitly granted by another agent.
/// </summary>
public interface IScopedVaultService : IVaultService
{
    string AgentName { get; }

    /// <summary>
    /// Grants another agent read (and optionally write) access to a specific path prefix.
    /// Only agents that own the path prefix may grant access to it.
    /// Implemented by agents that act as secret custodians (e.g. SAM for /databases/*).
    /// </summary>
    Task GrantAccessAsync(
        string granteeAgent,
        string pathPrefix,
        bool canWrite = false,
        CancellationToken ct = default) =>
        throw new NotSupportedException($"Agent '{AgentName}' does not support vault grants.");

    /// <summary>
    /// Revokes a previously granted access entry.
    /// </summary>
    Task RevokeAccessAsync(
        string granteeAgent,
        string pathPrefix,
        CancellationToken ct = default) =>
        throw new NotSupportedException($"Agent '{AgentName}' does not support vault revocation.");
}
