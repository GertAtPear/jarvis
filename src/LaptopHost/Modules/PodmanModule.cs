using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LaptopHost.Modules;

public class PodmanModule(ILogger<PodmanModule> logger) : ILaptopToolModule
{
    private static readonly JsonSerializerOptions Opts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public string ModuleName => "podman";

    public IEnumerable<LaptopToolSpec> GetDefinitions() =>
    [
        new("laptop_podman_ps",
            "List running (or all) Podman containers on the laptop",
            """{"type":"object","properties":{"all":{"type":"boolean","description":"Include stopped containers"}}}"""),

        new("laptop_podman_prune",
            "Prune stopped containers, dangling images, or unused volumes",
            """{"type":"object","properties":{"target":{"type":"string","enum":["containers","images","volumes","all"]}},"required":["target"]}"""),

        new("laptop_podman_logs",
            "Tail the logs of a container",
            """{"type":"object","properties":{"container":{"type":"string"},"lines":{"type":"integer","default":50}},"required":["container"]}"""),

        new("laptop_podman_build",
            "Build a Podman image from a Dockerfile context",
            """{"type":"object","properties":{"context_path":{"type":"string"},"tag":{"type":"string"}},"required":["context_path","tag"]}""",
            RequireConfirm: true),

        new("laptop_podman_stats",
            "Show CPU and memory usage per running container",
            """{"type":"object","properties":{}}""")
    ];

    public async Task<string> ExecuteAsync(string toolName, JsonDocument parameters, CancellationToken ct = default)
    {
        try
        {
            var root = parameters.RootElement;
            return toolName switch
            {
                "laptop_podman_ps"    => await RunPodmanAsync(BuildPs(root), ct),
                "laptop_podman_prune" => await RunPodmanAsync(BuildPrune(root), ct),
                "laptop_podman_logs"  => await RunPodmanAsync(BuildLogs(root), ct),
                "laptop_podman_build" => await RunPodmanAsync(BuildBuild(root), ct),
                "laptop_podman_stats" => await RunPodmanAsync("stats --no-stream --format json", ct),
                _ => Err($"Unknown tool: {toolName}")
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Podman] Tool '{Tool}' failed", toolName);
            return Err(ex.Message);
        }
    }

    private static string BuildPs(JsonElement p)
    {
        var all = p.TryGetProperty("all", out var a) && a.GetBoolean();
        return all ? "ps --all --format json" : "ps --format json";
    }

    private static string BuildPrune(JsonElement p)
    {
        var target = p.GetProperty("target").GetString()!;
        return target switch
        {
            "containers" => "container prune --force",
            "images"     => "image prune --force",
            "volumes"    => "volume prune --force",
            "all"        => "system prune --force",
            _ => throw new ArgumentException($"Invalid prune target: {target}")
        };
    }

    private static string BuildLogs(JsonElement p)
    {
        var container = p.GetProperty("container").GetString()!;
        var lines     = p.TryGetProperty("lines", out var l) ? l.GetInt32() : 50;
        return $"logs --tail {lines} {container}";
    }

    private static string BuildBuild(JsonElement p)
    {
        var context = p.GetProperty("context_path").GetString()!;
        var tag     = p.GetProperty("tag").GetString()!;
        return $"build --tag {tag} {context}";
    }

    private async Task<string> RunPodmanAsync(string args, CancellationToken ct)
    {
        logger.LogInformation("[Podman] podman {Args}", args);

        var psi = new ProcessStartInfo("podman", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start podman");

        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
            return Err($"podman exited {proc.ExitCode}: {stderr.Trim()}");

        return Ok(new { exit_code = proc.ExitCode, output = stdout.Trim(), args });
    }

    private static string Ok(object value)  => JsonSerializer.Serialize(value, Opts);
    private static string Err(string msg)   => JsonSerializer.Serialize(new { error = msg }, Opts);
}
