using System.Diagnostics;
using Browser.Agent.Models;
using Microsoft.Playwright;

namespace Browser.Agent.Services;

/// <summary>
/// Manages a single headless Chromium browser instance shared across requests.
/// A SemaphoreSlim caps concurrent sessions. Each request gets its own
/// BrowserContext (isolated cookies/storage) that is disposed after use.
///
/// SECURITY: This service executes arbitrary browser actions. It must ONLY be
/// accessible from within the Docker network (mediahost-ai bridge).
/// Nginx must NOT expose /agents/browser to the internet.
/// The Nginx config restricts /agents/ to internal IPs only.
/// </summary>
public sealed class PlaywrightService : IAsyncDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly int           _maxSessions;
    private readonly int           _defaultTimeoutMs;
    private readonly ILogger<PlaywrightService> _logger;

    private IPlaywright? _playwright;
    private IBrowser?    _browser;

    // Initialized once at startup; all operations await this before proceeding.
    private readonly Task _initTask;

    public int MaxSessions    => _maxSessions;
    public int ActiveSessions => _maxSessions - _semaphore.CurrentCount;

    public PlaywrightService(IConfiguration config, ILogger<PlaywrightService> logger)
    {
        _logger           = logger;
        _maxSessions      = config.GetValue<int>("Browser:MaxSessions", 3);
        _defaultTimeoutMs = config.GetValue<int>("Browser:DefaultTimeoutMs", 30_000);
        _semaphore        = new SemaphoreSlim(_maxSessions, _maxSessions);
        _initTask         = InitAsync();
    }

    private async Task InitAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser    = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args     = ["--no-sandbox", "--disable-setuid-sandbox",
                        "--disable-dev-shm-usage", "--disable-gpu"]
        });
        _logger.LogInformation("Browser.Agent: Chromium started (max {Max} concurrent sessions)", _maxSessions);
    }

    // ── Public operations ─────────────────────────────────────────────────────

    public Task<BrowserResult> NavigateAndScreenshotAsync(
        string url, string? waitForSelector, CancellationToken ct = default)
        => RunAsync(url, async page =>
        {
            await NavigateAsync(page, url, ct);

            if (!string.IsNullOrEmpty(waitForSelector))
                await page.WaitForSelectorAsync(waitForSelector,
                    new PageWaitForSelectorOptions { Timeout = _defaultTimeoutMs });

            var screenshot = await page.ScreenshotAsync(
                new PageScreenshotOptions { Type = ScreenshotType.Png, FullPage = false });

            return (null, Convert.ToBase64String(screenshot));
        }, ct);

    public Task<BrowserResult> NavigateAndExtractTextAsync(
        string url, string? cssSelector, CancellationToken ct = default)
        => RunAsync(url, async page =>
        {
            await NavigateAsync(page, url, ct);

            string text;
            if (!string.IsNullOrEmpty(cssSelector))
            {
                await page.WaitForSelectorAsync(cssSelector,
                    new PageWaitForSelectorOptions { Timeout = _defaultTimeoutMs });
                text = await page.InnerTextAsync(cssSelector);
            }
            else
            {
                text = await page.InnerTextAsync("body");
            }

            return (text, null);
        }, ct);

    public Task<BrowserResult> NavigateAndClickAsync(
        string url, string cssSelector, CancellationToken ct = default)
        => RunAsync(url, async page =>
        {
            await NavigateAsync(page, url, ct);
            await page.WaitForSelectorAsync(cssSelector,
                new PageWaitForSelectorOptions { Timeout = _defaultTimeoutMs });
            await page.ClickAsync(cssSelector);

            // Wait briefly for any navigation or DOM update triggered by the click
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                new PageWaitForLoadStateOptions { Timeout = 5_000 }).ConfigureAwait(false);

            var screenshot = await page.ScreenshotAsync(
                new PageScreenshotOptions { Type = ScreenshotType.Png });

            return (null, Convert.ToBase64String(screenshot));
        }, ct);

    public Task<BrowserResult> FillFormAsync(
        string url, Dictionary<string, string> fields, string submitSelector,
        CancellationToken ct = default)
        => RunAsync(url, async page =>
        {
            await NavigateAsync(page, url, ct);

            foreach (var (selector, value) in fields)
            {
                await page.WaitForSelectorAsync(selector,
                    new PageWaitForSelectorOptions { Timeout = _defaultTimeoutMs });
                await page.FillAsync(selector, value);
            }

            await page.ClickAsync(submitSelector);

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                new PageWaitForLoadStateOptions { Timeout = _defaultTimeoutMs }).ConfigureAwait(false);

            var screenshot = await page.ScreenshotAsync(
                new PageScreenshotOptions { Type = ScreenshotType.Png });

            return (null, Convert.ToBase64String(screenshot));
        }, ct);

    public Task<BrowserResult> RunScriptAsync(
        string url, string javascriptExpression, CancellationToken ct = default)
        => RunAsync(url, async page =>
        {
            await NavigateAsync(page, url, ct);

            var result = await page.EvaluateAsync<object>(javascriptExpression);
            var text   = result?.ToString();

            var screenshot = await page.ScreenshotAsync(
                new PageScreenshotOptions { Type = ScreenshotType.Png });

            return (text, Convert.ToBase64String(screenshot));
        }, ct);

    // ── Core execution wrapper ────────────────────────────────────────────────

    private async Task<BrowserResult> RunAsync(
        string url,
        Func<IPage, Task<(string? extractedText, string? screenshotBase64)>> action,
        CancellationToken ct)
    {
        await _initTask;   // ensure browser is ready

        await _semaphore.WaitAsync(ct);
        var sw = Stopwatch.StartNew();
        IBrowserContext? context = null;

        try
        {
            context = await _browser!.NewContextAsync(new BrowserNewContextOptions
            {
                IgnoreHTTPSErrors = true
            });
            context.SetDefaultTimeout(_defaultTimeoutMs);

            var page = await context.NewPageAsync();

            var (extractedText, screenshotBase64) = await action(page);

            sw.Stop();
            return new BrowserResult(
                Success:          true,
                ErrorMessage:     null,
                ScreenshotBase64: screenshotBase64,
                ExtractedText:    extractedText,
                PageTitle:        await page.TitleAsync(),
                PageUrl:          page.Url,
                DurationMs:       (int)sw.ElapsedMilliseconds
            );
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Browser action failed for URL: {Url}", url);
            return new BrowserResult(
                Success:          false,
                ErrorMessage:     ex.Message,
                ScreenshotBase64: null,
                ExtractedText:    null,
                PageTitle:        null,
                PageUrl:          url,
                DurationMs:       (int)sw.ElapsedMilliseconds
            );
        }
        finally
        {
            if (context is not null)
                await context.CloseAsync();
            _semaphore.Release();
        }
    }

    private static async Task NavigateAsync(IPage page, string url, CancellationToken ct)
    {
        var response = await page.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded
        });

        if (response is not null && !response.Ok && response.Status != 0)
            throw new InvalidOperationException(
                $"Navigation failed: HTTP {response.Status} for {url}");
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
            await _browser.CloseAsync();
        _playwright?.Dispose();
        _semaphore.Dispose();
    }
}
