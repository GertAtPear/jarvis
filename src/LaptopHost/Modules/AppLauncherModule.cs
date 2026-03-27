using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LaptopHost.Modules;

public class AppLauncherModule(ILogger<AppLauncherModule> logger) : ILaptopToolModule
{
    private static readonly JsonSerializerOptions Opts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public string ModuleName => "app_launcher";

    public IEnumerable<LaptopToolSpec> GetDefinitions() =>
    [
        new("laptop_open_file",
            "Open a file in its default application (e.g. open a PDF, image, or spreadsheet)",
            """{"type":"object","properties":{"path":{"type":"string","description":"Full path to the file to open"}},"required":["path"]}"""),

        new("laptop_open_url",
            "Open a URL in the system's default web browser",
            """{"type":"object","properties":{"url":{"type":"string","description":"The URL to open"}},"required":["url"]}"""),

        new("laptop_open_app",
            "Launch an installed application by name",
            """{"type":"object","properties":{"app_name":{"type":"string","description":"Application name (e.g. 'code', 'firefox', 'nautilus')"}},"required":["app_name"]}""")
    ];

    public Task<string> ExecuteAsync(string toolName, JsonDocument parameters, CancellationToken ct = default)
    {
        try
        {
            var root = parameters.RootElement;
            return Task.FromResult(toolName switch
            {
                "laptop_open_file" => OpenFile(root),
                "laptop_open_url"  => OpenUrl(root),
                "laptop_open_app"  => OpenApp(root),
                _ => Err($"Unknown tool: {toolName}")
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppLauncher] Tool '{Tool}' failed", toolName);
            return Task.FromResult(Err(ex.Message));
        }
    }

    private string OpenFile(JsonElement p)
    {
        var path = p.GetProperty("path").GetString()!;
        if (!File.Exists(path))
            return Err($"File not found: {path}");

        LaunchDetached(null, path);
        logger.LogInformation("[AppLauncher] Opened file: {Path}", path);
        return Ok(new { opened = path });
    }

    private string OpenUrl(JsonElement p)
    {
        var url = p.GetProperty("url").GetString()!;
        LaunchDetached(null, url);
        logger.LogInformation("[AppLauncher] Opened URL: {Url}", url);
        return Ok(new { opened = url });
    }

    private string OpenApp(JsonElement p)
    {
        var app = p.GetProperty("app_name").GetString()!;
        LaunchDetached(app, null);
        logger.LogInformation("[AppLauncher] Launched app: {App}", app);
        return Ok(new { launched = app });
    }

    private static void LaunchDetached(string? fileName, string? args)
    {
        var psi = new ProcessStartInfo
        {
            UseShellExecute = true
        };

        if (OperatingSystem.IsLinux())
        {
            // On Linux: xdg-open for files/URLs, direct exec for apps
            if (fileName is not null && args is null)
            {
                psi.FileName  = fileName;
            }
            else
            {
                psi.FileName  = "xdg-open";
                psi.Arguments = args ?? fileName ?? "";
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            psi.FileName  = "open";
            psi.Arguments = args ?? fileName ?? "";
        }
        else
        {
            psi.FileName  = fileName ?? "cmd";
            psi.Arguments = args is not null ? $"/c start {args}" : "";
        }

        Process.Start(psi);
    }

    private static string Ok(object value)  => JsonSerializer.Serialize(value, Opts);
    private static string Err(string msg)   => JsonSerializer.Serialize(new { error = msg }, Opts);
}
