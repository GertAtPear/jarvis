namespace Mediahost.Tools.Models;

public record SshCredentials(
    string Username,
    string? Password,
    string? KeyFilePath)
{
    public bool IsKeyBased => !string.IsNullOrWhiteSpace(KeyFilePath);

    public static SshCredentials FromPassword(string username, string password) =>
        new(username, password, null);

    public static SshCredentials FromKeyFile(string username, string keyFilePath) =>
        new(username, null, keyFilePath);
}
