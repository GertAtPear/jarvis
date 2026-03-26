using Andrew.Agent.Models;

namespace Andrew.Agent.Jobs.CheckExecutors;

/// <summary>
/// HTTP(S) liveness check. Sends a GET request and considers the site up
/// if the response is any 2xx or 3xx status code.
/// </summary>
public class WebsiteUpCheckExecutor(
    IHttpClientFactory httpClientFactory,
    ILogger<WebsiteUpCheckExecutor> logger)
{
    public async Task<(bool ok, string details)> ExecuteAsync(
        ScheduledCheck check, CancellationToken ct)
    {
        var url = check.Target;

        // Ensure the target has a scheme
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var client = httpClientFactory.CreateClient("website-check");

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();

            var statusCode = (int)response.StatusCode;
            var ok = statusCode is >= 200 and < 400;

            return ok
                ? (true,  $"{url} responded {statusCode} in {sw.ElapsedMilliseconds}ms")
                : (false, $"{url} returned HTTP {statusCode} ({response.ReasonPhrase}) in {sw.ElapsedMilliseconds}ms");
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            return (false, $"{url} is unreachable: {ex.Message} ({sw.ElapsedMilliseconds}ms)");
        }
        catch (TaskCanceledException)
        {
            sw.Stop();
            return (false, $"{url} timed out after {sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogDebug(ex, "Website check error for {Url}", url);
            return (false, $"{url} check failed: {ex.Message}");
        }
    }
}
