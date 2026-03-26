namespace Mediahost.Tools.Models;

public record WinRmCredentials(
    string Username,
    string Password,
    bool UseHttps = false);
