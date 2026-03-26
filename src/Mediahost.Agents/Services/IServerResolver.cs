namespace Mediahost.Agents.Services;

/// <summary>
/// Resolves a server name/IP to its connection details and vault secrets.
/// Implement this in each agent to back server lookup with their own registry.
/// </summary>
public interface IServerResolver
{
    /// <summary>
    /// Resolves a server query (hostname, IP, or display name) to connection details.
    /// Returns null if the server is not found.
    /// </summary>
    Task<ServerConnectionInfo?> ResolveAsync(string serverQuery, CancellationToken ct = default);
}

/// <summary>Connection details for a resolved server.</summary>
public record ServerConnectionInfo(
    /// <summary>Registered hostname (used for display and vault paths).</summary>
    string Hostname,
    /// <summary>IP or hostname to actually connect to.</summary>
    string Host,
    /// <summary>All vault secrets stored at /servers/{hostname}.</summary>
    Dictionary<string, string> Secrets);
