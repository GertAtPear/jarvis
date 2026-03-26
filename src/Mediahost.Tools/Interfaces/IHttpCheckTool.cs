using Mediahost.Tools.Models;

namespace Mediahost.Tools.Interfaces;

public record HttpCheckResult(
    bool IsUp,
    int? StatusCode,
    long ResponseTimeMs,
    string? RedirectUrl,
    string? ErrorMessage);

public interface IHttpCheckTool
{
    /// <summary>
    /// Performs an HTTP GET and returns reachability info.
    /// IsUp=true for 2xx/3xx; false for 5xx, timeout, or connection refused.
    /// </summary>
    Task<ToolResult<HttpCheckResult>> CheckAsync(
        string url,
        int timeoutSeconds = 10,
        bool followRedirects = true,
        CancellationToken ct = default);
}
