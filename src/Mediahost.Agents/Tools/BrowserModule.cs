using System.Text.Json;
using Mediahost.Llm.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Mediahost.Agents.Tools;

/// <summary>
/// Shared tool module that wraps the Browser.Agent HTTP API.
/// Agents register this module to gain Playwright browser automation tools.
///
/// Register in any agent's DI setup:
///   services.AddScoped&lt;IToolModule, BrowserModule&gt;();
///
/// Requires configuration: Browser:BaseUrl (default: http://browser-agent:5004)
/// </summary>
public class BrowserModule(
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ILogger<BrowserModule> logger) : IToolModule
{
    private static readonly JsonSerializerOptions Opts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly JsonSerializerOptions ReadOpts =
        new() { PropertyNameCaseInsensitive = true };

    public IEnumerable<ToolDefinition> GetDefinitions() =>
    [
        new ToolDefinition(
            "browser_screenshot",
            "Take a screenshot of a web page and return it as a base64-encoded PNG. " +
            "Use for visual inspection of internal dashboards, monitoring UIs, or any web page that cannot be accessed via text scraping.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "url":               { "type": "string",  "description": "The full URL to navigate to" },
                "wait_for_selector": { "type": "string",  "description": "Optional CSS selector to wait for before screenshotting" }
              },
              "required": ["url"]
            }
            """)),

        new ToolDefinition(
            "browser_extract",
            "Navigate to a URL and extract text content from the page. Optionally scope to a CSS selector. " +
            "Use for scraping behind authenticated pages that the LLM cannot access directly.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "url":          { "type": "string", "description": "The full URL to navigate to" },
                "css_selector": { "type": "string", "description": "Optional CSS selector to extract text from (defaults to body)" }
              },
              "required": ["url"]
            }
            """)),

        new ToolDefinition(
            "browser_fill_form",
            "Navigate to a URL, fill form fields by CSS selector, and click a submit button. " +
            "Returns a screenshot of the page after submission.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "url":             { "type": "string", "description": "The full URL of the page containing the form" },
                "fields":          { "type": "object", "description": "Map of CSS selector → value to fill", "additionalProperties": { "type": "string" } },
                "submit_selector": { "type": "string", "description": "CSS selector of the submit button or element to click" }
              },
              "required": ["url", "fields", "submit_selector"]
            }
            """)),

        new ToolDefinition(
            "browser_script",
            "Navigate to a URL and run a JavaScript expression in the page context. Returns the expression result as a string plus a screenshot.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "url":        { "type": "string", "description": "The full URL to navigate to" },
                "javascript": { "type": "string", "description": "JavaScript expression to evaluate in the page context" }
              },
              "required": ["url", "javascript"]
            }
            """))
    ];

    public async Task<string?> TryExecuteAsync(string toolName, JsonDocument input, CancellationToken ct = default)
    {
        try
        {
            return toolName switch
            {
                "browser_screenshot" => await ScreenshotAsync(input, ct),
                "browser_extract"    => await ExtractAsync(input, ct),
                "browser_fill_form"  => await FillFormAsync(input, ct),
                "browser_script"     => await ScriptAsync(input, ct),
                _ => null   // not our tool
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[BrowserModule] Tool '{Tool}' failed", toolName);
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    // ── Tool implementations ──────────────────────────────────────────────────

    private async Task<string> ScreenshotAsync(JsonDocument input, CancellationToken ct)
    {
        var root     = input.RootElement;
        var url      = root.GetProperty("url").GetString()!;
        var selector = root.TryGetProperty("wait_for_selector", out var s) ? s.GetString() : null;

        var body = new { Url = url, WaitForSelector = selector, SessionContext = "agent-browser" };
        return await PostAsync("/api/browser/screenshot", body, ct);
    }

    private async Task<string> ExtractAsync(JsonDocument input, CancellationToken ct)
    {
        var root     = input.RootElement;
        var url      = root.GetProperty("url").GetString()!;
        var selector = root.TryGetProperty("css_selector", out var s) ? s.GetString() : null;

        var body = new { Url = url, CssSelector = selector };
        return await PostAsync("/api/browser/extract", body, ct);
    }

    private async Task<string> FillFormAsync(JsonDocument input, CancellationToken ct)
    {
        var root           = input.RootElement;
        var url            = root.GetProperty("url").GetString()!;
        var submitSelector = root.GetProperty("submit_selector").GetString()!;

        var fields = new Dictionary<string, string>();
        if (root.TryGetProperty("fields", out var fieldsEl))
            foreach (var prop in fieldsEl.EnumerateObject())
                fields[prop.Name] = prop.Value.GetString() ?? "";

        var body = new { Url = url, Fields = fields, SubmitSelector = submitSelector };
        return await PostAsync("/api/browser/fill-form", body, ct);
    }

    private async Task<string> ScriptAsync(JsonDocument input, CancellationToken ct)
    {
        var root   = input.RootElement;
        var url    = root.GetProperty("url").GetString()!;
        var script = root.GetProperty("javascript").GetString()!;

        var body = new { Url = url, Script = script };
        return await PostAsync("/api/browser/script", body, ct);
    }

    // ── HTTP helper ───────────────────────────────────────────────────────────

    private async Task<string> PostAsync(string path, object body, CancellationToken ct)
    {
        var baseUrl = config["Browser:BaseUrl"] ?? "http://browser-agent:5004";
        var client  = httpFactory.CreateClient();
        client.BaseAddress = new Uri(baseUrl);
        client.Timeout     = TimeSpan.FromSeconds(60);

        var json     = JsonSerializer.Serialize(body, Opts);
        var content  = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync(path, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Serialize(new
            {
                error      = $"Browser agent returned HTTP {(int)response.StatusCode}",
                detail     = err
            });
        }

        return await response.Content.ReadAsStringAsync(ct);
    }
}
