namespace Mediahost.Tools.Models;

public enum OsType { Unknown, Linux, Windows, MacOs }

public record ConnectionTarget(
    string Hostname,
    string? IpAddress,
    int Port,
    OsType Os = OsType.Unknown)
{
    /// <summary>Prefer IP address over hostname for the actual TCP connection.</summary>
    public string ResolvedHost =>
        string.IsNullOrWhiteSpace(IpAddress) ? Hostname : IpAddress;
}
