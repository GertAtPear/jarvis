using Mediahost.Shared.Services;
using Mediahost.Tools.Models;

namespace Mediahost.Agents.Helpers;

/// <summary>
/// Static helpers that know the Mediahost vault path conventions.
/// Agents call these instead of knowing the vault paths themselves.
/// </summary>
public static class CredentialHelper
{
    /// <summary>
    /// Fetches SSH credentials for a server from /servers/{hostname}.
    /// Returns null if credentials are missing or vault access fails.
    /// </summary>
    public static async Task<SshCredentials?> GetSshCredentialsAsync(
        IVaultService vault, string hostname, CancellationToken ct = default)
    {
        Dictionary<string, string> secrets;
        try { secrets = await vault.GetSecretsBulkAsync($"/servers/{hostname}", ct); }
        catch { return null; }

        if (!secrets.TryGetValue("ssh_user", out var user))
            user = "root";

        if (secrets.TryGetValue("ssh_key_path", out var keyPath) && !string.IsNullOrWhiteSpace(keyPath))
            return SshCredentials.FromKeyFile(user, keyPath);

        if (secrets.TryGetValue("ssh_password", out var pass) && !string.IsNullOrWhiteSpace(pass))
            return SshCredentials.FromPassword(user, pass);

        return null;
    }

    /// <summary>
    /// Fetches WinRM credentials from /servers/{hostname}.
    /// Returns null if credentials are missing or vault access fails.
    /// </summary>
    public static async Task<WinRmCredentials?> GetWinRmCredentialsAsync(
        IVaultService vault, string hostname, CancellationToken ct = default)
    {
        Dictionary<string, string> secrets;
        try { secrets = await vault.GetSecretsBulkAsync($"/servers/{hostname}", ct); }
        catch { return null; }

        if (!secrets.TryGetValue("winrm_user", out var user) || string.IsNullOrWhiteSpace(user))
            return null;
        if (!secrets.TryGetValue("winrm_password", out var pass) || string.IsNullOrWhiteSpace(pass))
            return null;

        var useHttps = secrets.TryGetValue("winrm_https", out var h) &&
                       string.Equals(h, "true", StringComparison.OrdinalIgnoreCase);

        return new WinRmCredentials(user, pass, useHttps);
    }

    /// <summary>
    /// Fetches PostgreSQL credentials from the given vault path.
    /// Expected keys: pg_host, pg_port, pg_database, pg_user, pg_password.
    /// Returns null if required keys are missing.
    /// </summary>
    public static async Task<SqlCredentials?> GetPostgresCredentialsAsync(
        IVaultService vault, string vaultPath, CancellationToken ct = default)
    {
        Dictionary<string, string> secrets;
        try { secrets = await vault.GetSecretsBulkAsync(vaultPath, ct); }
        catch { return null; }

        if (!secrets.TryGetValue("pg_host", out var host) || string.IsNullOrWhiteSpace(host)) return null;
        if (!secrets.TryGetValue("pg_user", out var user) || string.IsNullOrWhiteSpace(user)) return null;
        if (!secrets.TryGetValue("pg_password", out var pass) || string.IsNullOrWhiteSpace(pass)) return null;

        secrets.TryGetValue("pg_database", out var db);
        var port = secrets.TryGetValue("pg_port", out var portStr) && int.TryParse(portStr, out var p) ? p : 5432;

        return new SqlCredentials(host, port, db ?? user, user, pass, DatabaseType.PostgreSQL);
    }

    /// <summary>
    /// Fetches MySQL/MariaDB credentials from the given vault path.
    /// Expected keys: mysql_host, mysql_port, mysql_database, mysql_user, mysql_password.
    /// Returns null if required keys are missing.
    /// </summary>
    public static async Task<SqlCredentials?> GetMySqlCredentialsAsync(
        IVaultService vault, string vaultPath, CancellationToken ct = default)
    {
        Dictionary<string, string> secrets;
        try { secrets = await vault.GetSecretsBulkAsync(vaultPath, ct); }
        catch { return null; }

        if (!secrets.TryGetValue("mysql_host", out var host) || string.IsNullOrWhiteSpace(host)) return null;
        if (!secrets.TryGetValue("mysql_user", out var user) || string.IsNullOrWhiteSpace(user)) return null;
        if (!secrets.TryGetValue("mysql_password", out var pass) || string.IsNullOrWhiteSpace(pass)) return null;

        secrets.TryGetValue("mysql_database", out var db);
        var port = secrets.TryGetValue("mysql_port", out var portStr) && int.TryParse(portStr, out var p) ? p : 3306;

        return new SqlCredentials(host, port, db ?? user, user, pass, DatabaseType.MySQL);
    }
}
