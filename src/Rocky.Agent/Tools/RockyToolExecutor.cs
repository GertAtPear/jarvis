using System.Text.Json;
using Mediahost.Agents.Capabilities;
using Mediahost.Tools.Models;
using Microsoft.Extensions.Logging;
using Rocky.Agent.Data.Repositories;
using Rocky.Agent.Models;
using Rocky.Agent.Services;

namespace Rocky.Agent.Tools;

/// <summary>
/// Executes the 8 Rocky tools. Injected into RockyAgentService.
/// All tools are strictly read-only.
/// </summary>
public class RockyToolExecutor(
    WatchedServiceRepository serviceRepo,
    CheckResultRepository checkRepo,
    AlertRepository alertRepo,
    HttpCapability http,
    SshCapability ssh,
    DockerCapability docker,
    ILogger<RockyToolExecutor> logger)
{
    private static readonly JsonSerializerOptions Opts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task<string> ExecuteAsync(
        string toolName,
        IReadOnlyDictionary<string, JsonElement> parameters,
        CancellationToken ct = default)
    {
        try
        {
            return toolName switch
            {
                "get_service_status"    => await GetServiceStatusAsync(parameters, ct),
                "list_services"         => await ListServicesAsync(parameters, ct),
                "get_check_history"     => await GetCheckHistoryAsync(parameters, ct),
                "get_active_alerts"     => await GetActiveAlertsAsync(ct),
                "run_http_check"        => await RunHttpCheckAsync(parameters, ct),
                "run_tcp_check"         => await RunTcpCheckAsync(parameters, ct),
                "run_container_check"   => await RunContainerCheckAsync(parameters, ct),
                "run_ssh_process_check" => await RunSshProcessCheckAsync(parameters, ct),
                _ => Err($"Unknown tool: {toolName}")
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Rocky] Tool '{Tool}' threw unexpectedly", toolName);
            return Err(ex.Message);
        }
    }

    // ── Tool implementations ──────────────────────────────────────────────────

    private async Task<string> GetServiceStatusAsync(
        IReadOnlyDictionary<string, JsonElement> p, CancellationToken ct)
    {
        var name    = Str(p, "service_name");
        var service = await serviceRepo.GetByNameAsync(name);
        if (service is null)
        {
            // Try display_name search
            var all = await serviceRepo.GetAllAsync();
            service = all.FirstOrDefault(s =>
                s.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                s.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        }
        if (service is null) return Err($"No service found matching '{name}'.");

        var latest = await checkRepo.GetLatestAsync(service.Id);
        return Ok(new
        {
            service = new { service.Id, service.Name, service.DisplayName, service.CheckType, service.IntervalSeconds, service.Enabled },
            latest_check = latest is null ? null : new
            {
                is_healthy  = latest.IsHealthy,
                detail      = latest.Detail,
                duration_ms = latest.DurationMs,
                checked_at  = latest.CheckedAt
            }
        });
    }

    private async Task<string> ListServicesAsync(
        IReadOnlyDictionary<string, JsonElement> p, CancellationToken ct)
    {
        var unhealthyOnly = p.TryGetValue("unhealthy_only", out var u) && u.GetBoolean();
        var services = (await serviceRepo.GetAllAsync()).ToList();
        var results  = new List<object>();

        foreach (var svc in services)
        {
            var latest = await checkRepo.GetLatestAsync(svc.Id);
            if (unhealthyOnly && (latest is null || latest.IsHealthy)) continue;

            results.Add(new
            {
                name         = svc.Name,
                display_name = svc.DisplayName,
                check_type   = svc.CheckType,
                enabled      = svc.Enabled,
                is_healthy   = latest?.IsHealthy,
                detail       = latest?.Detail,
                checked_at   = latest?.CheckedAt
            });
        }

        return Ok(new { count = results.Count, services = results });
    }

    private async Task<string> GetCheckHistoryAsync(
        IReadOnlyDictionary<string, JsonElement> p, CancellationToken ct)
    {
        var name  = Str(p, "service_name");
        var limit = p.TryGetValue("limit", out var l) ? Math.Min(l.GetInt32(), 100) : 20;

        var service = await serviceRepo.GetByNameAsync(name);
        if (service is null)
        {
            var all = await serviceRepo.GetAllAsync();
            service = all.FirstOrDefault(s => s.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase));
        }
        if (service is null) return Err($"No service found matching '{name}'.");

        var history = await checkRepo.GetRecentAsync(service.Id, limit);
        return Ok(new
        {
            service_name = service.Name,
            results      = history.Select(r => new
            {
                is_healthy  = r.IsHealthy,
                detail      = r.Detail,
                duration_ms = r.DurationMs,
                checked_at  = r.CheckedAt
            })
        });
    }

    private async Task<string> GetActiveAlertsAsync(CancellationToken ct)
    {
        var alerts = await alertRepo.GetUnresolvedAsync();
        var all    = await serviceRepo.GetAllAsync();
        var svcMap = all.ToDictionary(s => s.Id);

        return Ok(new
        {
            count  = alerts.Count(),
            alerts = alerts.Select(a => new
            {
                service_name = svcMap.TryGetValue(a.ServiceId, out var s) ? s.DisplayName : a.ServiceId.ToString(),
                severity     = a.Severity,
                message      = a.Message,
                created_at   = a.CreatedAt
            })
        });
    }

    private async Task<string> RunHttpCheckAsync(
        IReadOnlyDictionary<string, JsonElement> p, CancellationToken ct)
    {
        var url     = Str(p, "url");
        var timeout = p.TryGetValue("timeout_seconds", out var t) ? t.GetInt32() : 10;

        var result = await http.CheckAsync(url, timeout, followRedirects: true, ct);
        if (!result.Success)
            return Err(result.ErrorMessage ?? "HTTP check failed");

        var r = result.Value!;
        return Ok(new
        {
            url,
            is_up          = r.IsUp,
            status_code    = r.StatusCode,
            response_time_ms = r.ResponseTimeMs,
            redirect_url   = r.RedirectUrl,
            error          = r.ErrorMessage
        });
    }

    private async Task<string> RunTcpCheckAsync(
        IReadOnlyDictionary<string, JsonElement> p, CancellationToken ct)
    {
        var host    = Str(p, "host");
        var port    = p["port"].GetInt32();
        var timeout = p.TryGetValue("timeout_ms", out var t) ? t.GetInt32() : 5000;

        var result = await http.TcpProbeAsync(host, port, timeout, ct);
        if (!result.Success)
            return Err(result.ErrorMessage ?? "TCP probe failed");

        var r = result.Value!;
        return Ok(new
        {
            host,
            port,
            is_open       = r.IsOpen,
            response_time = r.ResponseTimeMs,
            error         = r.ErrorMessage
        });
    }

    private async Task<string> RunContainerCheckAsync(
        IReadOnlyDictionary<string, JsonElement> p, CancellationToken ct)
    {
        var serverName    = Str(p, "server");
        var containerName = Str(p, "container");

        var creds = await ssh.GetCredentialsAsync(serverName, ct);
        if (creds is null)
            return Err($"No SSH credentials found for server '{serverName}'.");

        var target   = new ConnectionTarget(serverName, null, 22, OsType.Linux);
        var result   = await docker.ListContainersAsync(target, creds, all: true, ct);
        if (!result.Success)
            return Err(result.ErrorMessage ?? "Docker list failed");

        var container = result.Value?.FirstOrDefault(c =>
            c.Name.Contains(containerName, StringComparison.OrdinalIgnoreCase) ||
            c.Id.StartsWith(containerName, StringComparison.OrdinalIgnoreCase));

        if (container is null)
            return Ok(new { server = serverName, container = containerName, found = false, is_running = false });

        return Ok(new
        {
            server        = serverName,
            container     = container.Name,
            id            = container.Id[..Math.Min(12, container.Id.Length)],
            found         = true,
            is_running    = container.State.Equals("running", StringComparison.OrdinalIgnoreCase),
            state         = container.State,
            image         = container.Image
        });
    }

    private async Task<string> RunSshProcessCheckAsync(
        IReadOnlyDictionary<string, JsonElement> p, CancellationToken ct)
    {
        var serverName     = Str(p, "server");
        var processPattern = Str(p, "process");

        var creds = await ssh.GetCredentialsAsync(serverName, ct);
        if (creds is null)
            return Err($"No SSH credentials found for server '{serverName}'.");

        var target = new ConnectionTarget(serverName, null, 22, OsType.Linux);
        var output = await ssh.RunAndReadAsync(target, creds,
            $"pgrep -a -f '{processPattern}' | head -5",
            SshPermission.ReadOnly, ct);

        if (output is null)
            return Err($"SSH command failed for server '{serverName}'.");

        var running   = !string.IsNullOrWhiteSpace(output);
        var processes = running
            ? output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(5).ToList()
            : [];

        return Ok(new
        {
            server    = serverName,
            pattern   = processPattern,
            is_running = running,
            processes
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Str(IReadOnlyDictionary<string, JsonElement> p, string key)
    {
        if (!p.TryGetValue(key, out var val))
            throw new ArgumentException($"Missing required parameter '{key}'.");
        return val.GetString() ?? throw new ArgumentException($"Parameter '{key}' must not be null.");
    }

    private static string Ok(object value)  => JsonSerializer.Serialize(value, Opts);
    private static string Err(string msg)   => JsonSerializer.Serialize(new { error = msg }, Opts);
}
