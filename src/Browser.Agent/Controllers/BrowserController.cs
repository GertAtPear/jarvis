using Browser.Agent.Models;
using Browser.Agent.Services;
using Microsoft.AspNetCore.Mvc;

namespace Browser.Agent.Controllers;

/// <summary>
/// Tool-execution service for browser automation.
/// This is NOT an LLM agent — it executes discrete browser actions
/// on behalf of other agents (Andrew, Eve) and Jarvis.
///
/// SECURITY: Accessible only from within the mediahost-ai Docker bridge network.
/// Nginx must NOT expose /agents/browser to the internet.
/// </summary>
[ApiController]
[Route("api/browser")]
public class BrowserController(
    PlaywrightService browser,
    ILogger<BrowserController> logger) : ControllerBase
{
    // POST /api/browser/screenshot
    [HttpPost("screenshot")]
    public async Task<ActionResult<BrowserResult>> Screenshot(
        [FromBody] ScreenshotRequest req,
        CancellationToken ct)
    {
        logger.LogInformation("Screenshot: {Url}", req.Url);
        var result = await browser.NavigateAndScreenshotAsync(req.Url, req.WaitForSelector, ct);
        return Ok(result);
    }

    // POST /api/browser/extract
    [HttpPost("extract")]
    public async Task<ActionResult<BrowserResult>> Extract(
        [FromBody] ExtractRequest req,
        CancellationToken ct)
    {
        logger.LogInformation("Extract: {Url}", req.Url);
        var result = await browser.NavigateAndExtractTextAsync(req.Url, req.CssSelector, ct);
        return Ok(result);
    }

    // POST /api/browser/click
    [HttpPost("click")]
    public async Task<ActionResult<BrowserResult>> Click(
        [FromBody] ClickRequest req,
        CancellationToken ct)
    {
        logger.LogInformation("Click: {Url} → {Selector}", req.Url, req.CssSelector);
        var result = await browser.NavigateAndClickAsync(req.Url, req.CssSelector, ct);
        return Ok(result);
    }

    // POST /api/browser/fill-form
    [HttpPost("fill-form")]
    public async Task<ActionResult<BrowserResult>> FillForm(
        [FromBody] FillFormRequest req,
        CancellationToken ct)
    {
        logger.LogInformation("FillForm: {Url} ({FieldCount} fields)", req.Url, req.Fields.Count);
        var result = await browser.FillFormAsync(req.Url, req.Fields, req.SubmitSelector, ct);
        return Ok(result);
    }

    // POST /api/browser/script
    [HttpPost("script")]
    public async Task<ActionResult<BrowserResult>> Script(
        [FromBody] ScriptRequest req,
        CancellationToken ct)
    {
        logger.LogInformation("Script: {Url}", req.Url);
        var result = await browser.RunScriptAsync(req.Url, req.Script, ct);
        return Ok(result);
    }

    // GET /health
    [HttpGet("/health")]
    public ActionResult Health() => Ok(new
    {
        status         = "healthy",
        activeSessions = browser.ActiveSessions,
        maxSessions    = browser.MaxSessions
    });
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

public record ScreenshotRequest(
    string  Url,
    string? WaitForSelector,
    string? SessionContext      // caller-supplied context label (logging only)
);

public record ExtractRequest(
    string  Url,
    string? CssSelector
);

public record ClickRequest(
    string Url,
    string CssSelector
);

public record FillFormRequest(
    string                      Url,
    Dictionary<string, string>  Fields,          // selector → value
    string                      SubmitSelector
);

public record ScriptRequest(
    string Url,
    string Script
);
