namespace Mediahost.Shared.Services;

public interface IVaultService
{
    Task<string?> GetSecretAsync(string path, string key, CancellationToken ct = default);
    Task<Dictionary<string, string>> GetSecretsBulkAsync(string path, CancellationToken ct = default);
    Task SetSecretAsync(string path, string key, string value, CancellationToken ct = default);
    Task<bool> SecretExistsAsync(string path, string key, CancellationToken ct = default);
}
