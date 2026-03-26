// CREDENTIAL SAFETY: Never log SshCredentials, WinRmCredentials, SqlCredentials, or any password/key values.
// Log only: hostname, operation name, duration, success/failure.

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Mediahost.Tools.Interfaces;
using Mediahost.Tools.Models;
using Microsoft.Extensions.Logging;

namespace Mediahost.Tools.Docker;

public sealed partial class DockerTool(ISshTool sshTool, ILogger<DockerTool> logger) : IDockerTool
{
    [GeneratedRegex(@"password|secret|key|token|pwd|pass", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveKeyPattern();

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public async Task<ToolResult<IReadOnlyList<DockerContainerSummary>>> ListContainersAsync(
        ConnectionTarget target,
        SshCredentials credentials,
        bool all = true,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var flag = all ? "-a" : "";
        var result = await sshTool.RunCommandAsync(
            target, credentials, $"docker ps {flag} --format '{{{{json .}}}}' 2>/dev/null", ct);

        sw.Stop();

        if (!result.Success)
            return ToolResult<IReadOnlyList<DockerContainerSummary>>.Fail(result.ErrorMessage!, sw.ElapsedMilliseconds);

        var containers = new List<DockerContainerSummary>();
        foreach (var line in (result.Value ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('{')) continue;

            try
            {
                var entry = JsonSerializer.Deserialize<DockerPsEntry>(trimmed, JsonOpts);
                if (entry is null) continue;
                containers.Add(new DockerContainerSummary(
                    entry.ID,
                    entry.Names.TrimStart('/').Split(',')[0].TrimStart('/'),
                    entry.Image,
                    entry.State,
                    entry.Ports,
                    entry.Labels,
                    entry.CreatedAt));
            }
            catch { /* best-effort */ }
        }

        logger.LogDebug("DockerTool listed {Count} containers on {Host} in {Ms}ms",
            containers.Count, target.Hostname, sw.ElapsedMilliseconds);
        return ToolResult<IReadOnlyList<DockerContainerSummary>>.Ok(containers, sw.ElapsedMilliseconds);
    }

    public async Task<ToolResult<DockerContainerDetail>> InspectContainerAsync(
        ConnectionTarget target,
        SshCredentials credentials,
        string containerIdOrName,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var result = await sshTool.RunCommandAsync(
            target, credentials,
            $"docker inspect {containerIdOrName} --format '{{{{json .Config}}}}' 2>/dev/null", ct);

        if (!result.Success)
        {
            sw.Stop();
            return ToolResult<DockerContainerDetail>.Fail(result.ErrorMessage!, sw.ElapsedMilliseconds);
        }

        try
        {
            var config = JsonSerializer.Deserialize<DockerConfigJson>(result.Value?.Trim() ?? "{}", JsonOpts);

            // Env vars — redact sensitive keys
            var envVars = new Dictionary<string, string>();
            if (config?.Env is not null)
            {
                foreach (var pair in config.Env)
                {
                    var idx = pair.IndexOf('=');
                    if (idx < 0) continue;
                    var k = pair[..idx];
                    var v = pair[(idx + 1)..];
                    envVars[k] = SensitiveKeyPattern().IsMatch(k) ? "***REDACTED***" : v;
                }
            }

            // Labels
            var labels = config?.Labels as IReadOnlyDictionary<string, string>
                         ?? new Dictionary<string, string>();

            // Ports: ExposedPorts keys are like "80/tcp"
            var ports = config?.ExposedPorts?.Keys.ToList() as IReadOnlyList<string>
                        ?? [];

            sw.Stop();
            var detail = new DockerContainerDetail(
                containerIdOrName, containerIdOrName, "", "", envVars, ports, labels);

            logger.LogDebug("DockerTool inspected {Container} on {Host} in {Ms}ms",
                containerIdOrName, target.Hostname, sw.ElapsedMilliseconds);
            return ToolResult<DockerContainerDetail>.Ok(detail, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning("DockerTool failed to parse inspect output for {Container} on {Host}: {Message}",
                containerIdOrName, target.Hostname, ex.Message);
            return ToolResult<DockerContainerDetail>.Fail(ex.Message, sw.ElapsedMilliseconds);
        }
    }

    public async Task<ToolResult<string>> GetContainerLogsAsync(
        ConnectionTarget target,
        SshCredentials credentials,
        string containerIdOrName,
        int tailLines = 100,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var result = await sshTool.RunCommandAsync(
            target, credentials,
            $"docker logs {containerIdOrName} --tail {tailLines} 2>&1", ct);
        sw.Stop();

        if (!result.Success)
            return ToolResult<string>.Fail(result.ErrorMessage!, sw.ElapsedMilliseconds);

        logger.LogDebug("DockerTool got logs for {Container} on {Host} in {Ms}ms",
            containerIdOrName, target.Hostname, sw.ElapsedMilliseconds);
        return ToolResult<string>.Ok(result.Value ?? "", sw.ElapsedMilliseconds);
    }

    public async Task<ToolResult<IReadOnlyList<DockerStats>>> GetStatsAsync(
        ConnectionTarget target,
        SshCredentials credentials,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var result = await sshTool.RunCommandAsync(
            target, credentials,
            "docker stats --no-stream --format '{{json .}}' 2>/dev/null", ct);

        sw.Stop();

        if (!result.Success)
            return ToolResult<IReadOnlyList<DockerStats>>.Fail(result.ErrorMessage!, sw.ElapsedMilliseconds);

        var stats = new List<DockerStats>();
        foreach (var line in (result.Value ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('{')) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<DockerStatsEntry>(trimmed, JsonOpts);
                if (entry is null) continue;

                stats.Add(new DockerStats(
                    entry.ID,
                    entry.Name,
                    ParseCpuPercent(entry.CPUPerc),
                    ParseMemoryBytes(entry.MemUsage, first: true),
                    ParseMemoryBytes(entry.MemUsage, first: false)));
            }
            catch { /* best-effort */ }
        }

        logger.LogDebug("DockerTool got stats for {Count} containers on {Host} in {Ms}ms",
            stats.Count, target.Hostname, sw.ElapsedMilliseconds);
        return ToolResult<IReadOnlyList<DockerStats>>.Ok(stats, sw.ElapsedMilliseconds);
    }

    // ── Parse helpers ─────────────────────────────────────────────────────────

    private static double ParseCpuPercent(string s)
    {
        var clean = s.TrimEnd('%');
        return double.TryParse(clean, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    /// <summary>Parses "used / limit" format from docker stats MemUsage field.</summary>
    private static long ParseMemoryBytes(string memUsage, bool first)
    {
        var parts = memUsage.Split('/');
        var raw = (first ? parts[0] : (parts.Length > 1 ? parts[1] : "0")).Trim();
        return ParseSizeToBytes(raw);
    }

    private static long ParseSizeToBytes(string s)
    {
        if (s.Length < 2) return 0;
        var unit = s[^2..].ToUpperInvariant();
        if (!double.TryParse(s[..^2].Trim(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var val))
        {
            // try single-char unit
            unit = s[^1..].ToUpperInvariant();
            if (!double.TryParse(s[..^1].Trim(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out val))
                return 0;
        }

        return unit switch
        {
            "GB" or "G" => (long)(val * 1_073_741_824),
            "MB" or "M" => (long)(val * 1_048_576),
            "KB" or "K" => (long)(val * 1024),
            "GIB"       => (long)(val * 1_073_741_824),
            "MIB"       => (long)(val * 1_048_576),
            "KIB"       => (long)(val * 1024),
            _           => (long)val
        };
    }

    // ── Private DTOs ──────────────────────────────────────────────────────────

    private sealed class DockerPsEntry
    {
        [JsonPropertyName("ID")] public string ID { get; init; } = "";
        [JsonPropertyName("Names")] public string Names { get; init; } = "";
        [JsonPropertyName("Image")] public string Image { get; init; } = "";
        [JsonPropertyName("State")] public string State { get; init; } = "";
        [JsonPropertyName("Ports")] public string Ports { get; init; } = "";
        [JsonPropertyName("Labels")] public string Labels { get; init; } = "";
        [JsonPropertyName("CreatedAt")] public string CreatedAt { get; init; } = "";
    }

    private sealed class DockerConfigJson
    {
        [JsonPropertyName("Env")] public string[]? Env { get; init; }
        [JsonPropertyName("Labels")] public Dictionary<string, string>? Labels { get; init; }
        [JsonPropertyName("ExposedPorts")] public Dictionary<string, object?>? ExposedPorts { get; init; }
    }

    private sealed class DockerStatsEntry
    {
        [JsonPropertyName("ID")] public string ID { get; init; } = "";
        [JsonPropertyName("Name")] public string Name { get; init; } = "";
        [JsonPropertyName("CPUPerc")] public string CPUPerc { get; init; } = "0%";
        [JsonPropertyName("MemUsage")] public string MemUsage { get; init; } = "0B / 0B";
    }
}
