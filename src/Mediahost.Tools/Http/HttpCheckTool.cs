// CREDENTIAL SAFETY: Never log SshCredentials, WinRmCredentials, SqlCredentials, or any password/key values.
// Log only: hostname, operation name, duration, success/failure.

using System.Diagnostics;
using Mediahost.Tools.Interfaces;
using Mediahost.Tools.Models;
using Microsoft.Extensions.Logging;

namespace Mediahost.Tools.Http;

public sealed class HttpCheckTool(IHttpClientFactory httpClientFactory, ILogger<HttpCheckTool> logger)
    : IHttpCheckTool
{
    public async Task<ToolResult<HttpCheckResult>> CheckAsync(
        string url,
        int timeoutSeconds = 10,
        bool followRedirects = true,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var client = httpClientFactory.CreateClient("healthcheck");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            sw.Stop();

            var statusCode = (int)response.StatusCode;
            var isUp = statusCode is >= 200 and < 400;
            var redirectUrl = response.RequestMessage?.RequestUri?.ToString() is { } final
                && !string.Equals(final, url, StringComparison.OrdinalIgnoreCase)
                ? final
                : null;

            logger.LogDebug("HTTP check {Url} → {Status} in {Ms}ms", url, statusCode, sw.ElapsedMilliseconds);
            return ToolResult<HttpCheckResult>.Ok(
                new HttpCheckResult(isUp, statusCode, sw.ElapsedMilliseconds, redirectUrl, null),
                sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            logger.LogDebug("HTTP check {Url} timed out after {Ms}ms", url, sw.ElapsedMilliseconds);
            return ToolResult<HttpCheckResult>.Ok(
                new HttpCheckResult(false, null, sw.ElapsedMilliseconds, null, $"Timed out after {timeoutSeconds}s"),
                sw.ElapsedMilliseconds);
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            logger.LogDebug("HTTP check {Url} failed in {Ms}ms: {Message}", url, sw.ElapsedMilliseconds, ex.Message);
            return ToolResult<HttpCheckResult>.Ok(
                new HttpCheckResult(false, (int?)ex.StatusCode, sw.ElapsedMilliseconds, null, ex.Message),
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning("HTTP check {Url} unexpected error in {Ms}ms: {Message}",
                url, sw.ElapsedMilliseconds, ex.Message);
            return ToolResult<HttpCheckResult>.Fail(ex.Message, sw.ElapsedMilliseconds);
        }
    }
}
