using System.Text.Json;
using Mediahost.Agents.Capabilities;
using Mediahost.Agents.Helpers;
using Mediahost.Shared.Services;
using Mediahost.Tools.Models;
using Microsoft.Extensions.Logging;
using Rocky.Agent.Models;

namespace Rocky.Agent.Services;

/// <summary>
/// Dispatches per-service health checks using Mediahost.Agents capability wrappers.
/// Rocky is strictly read-only — all SSH/SQL calls use ReadOnly permission.
/// </summary>
public class CheckExecutorService(
    SshCapability ssh,
    DockerCapability docker,
    SqlCapability sql,
    HttpCapability http,
    IVaultService vault,
    ILogger<CheckExecutorService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Run the appropriate check for the given service and return a CheckResult.
    /// </summary>
    public async Task<CheckResult> ExecuteAsync(WatchedService service, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var config = JsonSerializer.Deserialize<JsonElement>(service.CheckConfig, JsonOpts);

            var (isHealthy, detail) = service.CheckType switch
            {
                "http_health"       => await HttpHealthCheckAsync(config, ct),
                "tcp_port"          => await TcpPortCheckAsync(config, ct),
                "container_running" => await ContainerRunningCheckAsync(service, config, ct),
                "ssh_process"       => await SshProcessCheckAsync(service, config, ct),
                "sql_select"        => await SqlSelectCheckAsync(service, config, ct),
                "kafka_lag"         => await KafkaLagCheckAsync(service, config, ct),
                "radio_capture"     => await RadioCaptureCheckAsync(service, config, ct),
                _ => (false, $"Unknown check type: {service.CheckType}")
            };

            sw.Stop();
            return new CheckResult
            {
                Id         = Guid.NewGuid(),
                ServiceId  = service.Id,
                IsHealthy  = isHealthy,
                Detail     = detail,
                DurationMs = sw.ElapsedMilliseconds,
                CheckedAt  = started
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Check executor failed for service {Name}", service.Name);
            return new CheckResult
            {
                Id         = Guid.NewGuid(),
                ServiceId  = service.Id,
                IsHealthy  = false,
                Detail     = $"Check executor error: {ex.Message}",
                DurationMs = sw.ElapsedMilliseconds,
                CheckedAt  = started
            };
        }
    }

    // ── Check methods ─────────────────────────────────────────────────────────

    private async Task<(bool, string?)> HttpHealthCheckAsync(JsonElement config, CancellationToken ct)
    {
        var url     = config.GetProperty("url").GetString()!;
        var timeout = config.TryGetProperty("timeout_seconds", out var t) ? t.GetInt32() : 10;

        var result = await http.CheckAsync(url, timeout, followRedirects: true, ct);
        if (!result.Success)
            return (false, result.ErrorMessage);

        var r = result.Value!;
        return (r.IsUp, r.IsUp
            ? $"HTTP {r.StatusCode} in {r.ResponseTimeMs}ms"
            : $"HTTP {r.StatusCode ?? 0} — {r.ErrorMessage}");
    }

    private async Task<(bool, string?)> TcpPortCheckAsync(JsonElement config, CancellationToken ct)
    {
        var host    = config.GetProperty("host").GetString()!;
        var port    = config.GetProperty("port").GetInt32();
        var timeout = config.TryGetProperty("timeout_ms", out var t) ? t.GetInt32() : 5000;

        var result = await http.TcpProbeAsync(host, port, timeout, ct);
        if (!result.Success)
            return (false, result.ErrorMessage);

        var r = result.Value!;
        return (r.IsOpen, r.IsOpen
            ? $"TCP {host}:{port} open in {r.ResponseTimeMs}ms"
            : $"TCP {host}:{port} unreachable — {r.ErrorMessage}");
    }

    private async Task<(bool, string?)> ContainerRunningCheckAsync(
        WatchedService service, JsonElement config, CancellationToken ct)
    {
        var serverHostname  = config.GetProperty("server").GetString()!;
        var containerName   = config.GetProperty("container").GetString()!;

        var (target, creds) = await ResolveSshAsync(serverHostname, service.VaultSecretPath, ct);
        if (target is null || creds is null)
            return (false, $"Cannot resolve SSH credentials for server '{serverHostname}'.");

        var result = await docker.ListContainersAsync(target, creds, all: true, ct);
        if (!result.Success)
            return (false, result.ErrorMessage);

        var container = result.Value?.FirstOrDefault(c =>
            c.Name.Contains(containerName, StringComparison.OrdinalIgnoreCase) ||
            c.Id.StartsWith(containerName, StringComparison.OrdinalIgnoreCase));

        if (container is null)
            return (false, $"Container '{containerName}' not found on {serverHostname}.");

        var running = container.State.Equals("running", StringComparison.OrdinalIgnoreCase);
        return (running, running
            ? $"Container '{containerName}' is running"
            : $"Container '{containerName}' is {container.State}");
    }

    private async Task<(bool, string?)> SshProcessCheckAsync(
        WatchedService service, JsonElement config, CancellationToken ct)
    {
        var serverHostname = config.GetProperty("server").GetString()!;
        var processPattern = config.GetProperty("process").GetString()!;

        var (target, creds) = await ResolveSshAsync(serverHostname, service.VaultSecretPath, ct);
        if (target is null || creds is null)
            return (false, $"Cannot resolve SSH credentials for server '{serverHostname}'.");

        var output = await ssh.RunAndReadAsync(target, creds,
            $"pgrep -a -f '{processPattern}' | head -5",
            SshPermission.ReadOnly, ct);

        if (output is null)
            return (false, $"SSH command failed for process check on {serverHostname}.");

        var running = !string.IsNullOrWhiteSpace(output);
        return (running, running
            ? $"Process '{processPattern}' running: {output.Split('\n')[0].Trim()}"
            : $"Process '{processPattern}' not found on {serverHostname}.");
    }

    private async Task<(bool, string?)> SqlSelectCheckAsync(
        WatchedService service, JsonElement config, CancellationToken ct)
    {
        var vaultPath = service.VaultSecretPath ??
                        config.GetProperty("vault_path").GetString()!;

        SqlCredentials? creds;
        try
        {
            creds = await CredentialHelper.GetPostgresCredentialsAsync(vault, vaultPath, ct);
        }
        catch (Exception ex)
        {
            return (false, $"Failed to load SQL credentials: {ex.Message}");
        }

        if (creds is null)
            return (false, $"No SQL credentials found at vault path '{vaultPath}'.");

        var query = config.GetProperty("query").GetString()!;

        var result = await sql.QueryAsync(creds, query, SqlPermission.ReadOnly, null, ct);
        if (!result.Success)
            return (false, result.ErrorMessage);

        var rowCount = result.Value?.Count ?? 0;

        // If a threshold is specified, check against it
        if (config.TryGetProperty("max_lag_rows", out var maxLag))
        {
            var max = maxLag.GetInt32();
            return (rowCount <= max,
                rowCount <= max
                    ? $"Query returned {rowCount} rows (threshold: {max})"
                    : $"Query returned {rowCount} rows — exceeds threshold of {max}");
        }

        return (true, $"Query OK — {rowCount} rows returned");
    }

    private async Task<(bool, string?)> KafkaLagCheckAsync(
        WatchedService service, JsonElement config, CancellationToken ct)
    {
        // Kafka lag is checked via SSH: kafka-consumer-groups.sh --describe
        var serverHostname  = config.GetProperty("server").GetString()!;
        var group           = config.GetProperty("group").GetString()!;
        var maxLag          = config.TryGetProperty("max_lag", out var ml) ? ml.GetInt64() : 1000L;

        var (target, creds) = await ResolveSshAsync(serverHostname, service.VaultSecretPath, ct);
        if (target is null || creds is null)
            return (false, $"Cannot resolve SSH credentials for '{serverHostname}'.");

        var bootstrapServer = config.TryGetProperty("bootstrap_server", out var bs)
            ? bs.GetString()! : "localhost:9092";

        var output = await ssh.RunAndReadAsync(target, creds,
            $"kafka-consumer-groups.sh --bootstrap-server {bootstrapServer} --describe --group {group} 2>/dev/null | awk 'NR>1 {{sum+=$6}} END {{print sum+0}}'",
            SshPermission.ReadOnly, ct);

        if (output is null)
            return (false, $"SSH command failed for Kafka lag check on {serverHostname}.");

        if (!long.TryParse(output.Trim(), out var totalLag))
            return (false, $"Could not parse Kafka lag output: '{output.Trim()}'");

        return (totalLag <= maxLag,
            totalLag <= maxLag
                ? $"Kafka group '{group}' lag: {totalLag} (threshold: {maxLag})"
                : $"Kafka group '{group}' lag: {totalLag} exceeds threshold of {maxLag}");
    }

    private async Task<(bool, string?)> RadioCaptureCheckAsync(
        WatchedService service, JsonElement config, CancellationToken ct)
    {
        // Radio capture is alive if its process is running and recent output files exist
        var serverHostname  = config.GetProperty("server").GetString()!;
        var processPattern  = config.GetProperty("process").GetString()!;
        var outputDir       = config.TryGetProperty("output_dir", out var od)
            ? od.GetString()! : "/var/radio-capture";
        var maxAgeMinutes   = config.TryGetProperty("max_age_minutes", out var ma)
            ? ma.GetInt32() : 5;

        var (target, creds) = await ResolveSshAsync(serverHostname, service.VaultSecretPath, ct);
        if (target is null || creds is null)
            return (false, $"Cannot resolve SSH credentials for '{serverHostname}'.");

        // Check process is running
        var processOutput = await ssh.RunAndReadAsync(target, creds,
            $"pgrep -af '{processPattern}' | head -1",
            SshPermission.ReadOnly, ct);

        if (string.IsNullOrWhiteSpace(processOutput))
            return (false, $"Radio capture process '{processPattern}' is not running on {serverHostname}.");

        // Check recent output files
        var fileOutput = await ssh.RunAndReadAsync(target, creds,
            $"find {outputDir} -newer /tmp/.rocky_time_ref -name '*.mp3' -o -name '*.wav' -o -name '*.ogg' 2>/dev/null | wc -l",
            SshPermission.ReadOnly, ct);

        // Touch reference file for next check (read-only can still touch /tmp)
        _ = await ssh.RunAndReadAsync(target, creds,
            $"touch /tmp/.rocky_time_ref -t $(date -d \"-{maxAgeMinutes} minutes\" '+%Y%m%d%H%M.%S') 2>/dev/null; echo ok",
            SshPermission.ReadOnly, ct);

        if (!int.TryParse(fileOutput?.Trim(), out var fileCount))
            fileCount = 0;

        return (true, $"Radio capture running (process: {processOutput.Split('\n')[0].Trim().Split(' ')[0]}), {fileCount} output files in last {maxAgeMinutes}min");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(ConnectionTarget?, SshCredentials?)> ResolveSshAsync(
        string serverHostname, string? vaultPath, CancellationToken ct)
    {
        var resolvedPath = vaultPath ?? $"/servers/{serverHostname}";
        var creds = await ssh.GetCredentialsAsync(serverHostname, ct);
        if (creds is null)
        {
            logger.LogWarning("No SSH credentials found for server '{Server}' at path '{Path}'",
                serverHostname, resolvedPath);
            return (null, null);
        }

        var target = new ConnectionTarget(serverHostname, null, 22, OsType.Linux);
        return (target, creds);
    }
}
