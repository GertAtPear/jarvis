using System.Text.Json;
using Dapper;
using Mediahost.Agents.Capabilities;
using Mediahost.Tools.Models;
using Microsoft.Extensions.Logging;
using Rocky.Agent.Data;
using Rocky.Agent.Data.Repositories;
using Rocky.Agent.Models;
using Rocky.Agent.Services;
using StackExchange.Redis;

namespace Rocky.Agent.Tools;

/// <summary>
/// Executes Rocky tools. Injected into RockyAgentService.
/// </summary>
public class RockyToolExecutor(
    WatchedServiceRepository serviceRepo,
    CheckResultRepository checkRepo,
    AlertRepository alertRepo,
    HttpCapability http,
    SshCapability ssh,
    DockerCapability docker,
    DbConnectionFactory db,
    IConnectionMultiplexer redis,
    IRockyJobScheduler scheduler,
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
                "register_query_check"  => await RegisterQueryCheckAsync(parameters, ct),
                "list_query_checks"     => await ListQueryChecksAsync(ct),
                "delete_query_check"    => await DeleteQueryCheckAsync(parameters, ct),
                "list_alert_channels"           => await ListAlertChannelsAsync(ct),
                "configure_alert_channel"       => await ConfigureAlertChannelAsync(parameters, ct),
                "configure_agent_alert_channel" => await ConfigureAgentAlertChannelAsync(parameters, ct),
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

    // ── Custom Query Checks ───────────────────────────────────────────────────

    private async Task<string> RegisterQueryCheckAsync(
        IReadOnlyDictionary<string, JsonElement> p, CancellationToken ct)
    {
        var name               = Str(p, "name");
        var query              = Str(p, "query");
        var vaultPath          = Str(p, "vault_path");
        var thresholdOperator  = Str(p, "threshold_operator");
        var thresholdValue     = p["threshold_value"].GetDouble();

        var description = p.TryGetValue("description", out var d) ? d.GetString() : null;
        var dbType      = p.TryGetValue("db_type", out var dt) ? dt.GetString() ?? "postgres" : "postgres";

        if (!new[] { "lt", "lte", "gt", "gte", "eq", "neq" }.Contains(thresholdOperator))
            return Err($"Invalid threshold_operator '{thresholdOperator}'. Must be: lt, lte, gt, gte, eq, neq.");

        // Build check_config
        var configDict = new Dictionary<string, object>
        {
            ["query"]              = query,
            ["vault_path"]         = vaultPath,
            ["db_type"]            = dbType,
            ["threshold_operator"] = thresholdOperator,
            ["threshold_value"]    = thresholdValue,
        };

        // Schedule: cron or interval_minutes
        int intervalSeconds;
        if (p.TryGetValue("cron", out var cronEl))
        {
            var cron = cronEl.GetString() ?? "";
            configDict["cron"] = cron;
            intervalSeconds    = -1; // sentinel — Quartz will use cron trigger
        }
        else if (p.TryGetValue("interval_minutes", out var imEl))
        {
            intervalSeconds = imEl.GetInt32() * 60;
        }
        else
        {
            intervalSeconds = 3600; // default: hourly
        }

        var configJson = JsonSerializer.Serialize(configDict);
        var displayName = description ?? name;

        var service = await serviceRepo.UpsertAsync(
            name, displayName, "sql_select", configJson, intervalSeconds, vaultPath);

        await scheduler.RefreshJobScheduleAsync(service, ct);

        logger.LogInformation("[Rocky] Registered query check '{Name}' (db_type={Db})", name, dbType);

        var scheduleDesc = configDict.ContainsKey("cron")
            ? $"cron: {configDict["cron"]}"
            : $"every {intervalSeconds / 60} minute(s)";

        return Ok(new
        {
            name,
            db_type            = dbType,
            threshold          = $"{thresholdOperator} {thresholdValue}",
            schedule           = scheduleDesc,
            message            = $"Query check '{name}' registered. {scheduleDesc}."
        });
    }

    private async Task<string> ListQueryChecksAsync(CancellationToken ct)
    {
        var all = await serviceRepo.GetAllAsync();
        var queryChecks = all.Where(s => s.CheckType == "sql_select").ToList();
        var results     = new List<object>();

        foreach (var svc in queryChecks)
        {
            var latest = await checkRepo.GetLatestAsync(svc.Id);
            results.Add(new
            {
                name            = svc.Name,
                description     = svc.DisplayName,
                is_healthy      = latest?.IsHealthy,
                last_result     = latest?.Detail,
                last_checked_at = latest?.CheckedAt,
                enabled         = svc.Enabled
            });
        }

        return Ok(new { count = results.Count, query_checks = results });
    }

    private async Task<string> DeleteQueryCheckAsync(
        IReadOnlyDictionary<string, JsonElement> p, CancellationToken ct)
    {
        var name    = Str(p, "name");
        var service = await serviceRepo.GetByNameAsync(name);

        if (service is null)
            return Err($"No query check found with name '{name}'.");

        if (service.CheckType != "sql_select")
            return Err($"Service '{name}' is not a query check (check_type: {service.CheckType}).");

        await scheduler.UnscheduleServiceAsync(service.Id, ct);
        var deleted = await serviceRepo.DeleteByNameAsync(name);

        return deleted
            ? Ok(new { name, message = $"Query check '{name}' deleted and unscheduled." })
            : Err($"Failed to delete query check '{name}'.");
    }

    // ── Alert Channels ────────────────────────────────────────────────────────

    private async Task<string> ListAlertChannelsAsync(CancellationToken ct)
    {
        await using var conn = db.Create();
        var channels = await conn.QueryAsync("""
            SELECT channel_name, channel_type, min_severity,
                   agent_filter, alert_type_filter, is_active, created_at
            FROM jarvis_schema.alert_channels
            ORDER BY created_at
            """);
        var list = channels.ToList();
        return Ok(new { count = list.Count, channels = list });
    }

    private async Task<string> ConfigureAlertChannelAsync(
        IReadOnlyDictionary<string, JsonElement> p, CancellationToken ct)
    {
        var channelName     = Str(p, "channel_name");
        var channelType     = Str(p, "channel_type");
        var configJson      = Str(p, "config_json");
        var minSeverity     = p.TryGetValue("min_severity", out var ms) ? ms.GetString() : "high";
        var agentFilter     = p.TryGetValue("agent_filter", out var af) ? af.GetString() : null;
        var alertTypeFilter = p.TryGetValue("alert_type_filter", out var atf) ? atf.GetString() : null;

        if (channelType != "slack" && channelType != "email" && channelType != "agent")
            return Err($"Invalid channel_type '{channelType}'. Must be 'slack', 'email', or 'agent'.");

        // Validate config JSON
        try { JsonDocument.Parse(configJson); }
        catch { return Err("config_json is not valid JSON."); }

        // Convert comma-separated filters to arrays
        var agentArr     = string.IsNullOrWhiteSpace(agentFilter)
            ? null : agentFilter.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        var alertTypeArr = string.IsNullOrWhiteSpace(alertTypeFilter)
            ? null : alertTypeFilter.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();

        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            INSERT INTO jarvis_schema.alert_channels
                (channel_name, channel_type, config, min_severity, agent_filter, alert_type_filter, is_active)
            VALUES
                (@channelName, @channelType, @configJson::jsonb, @minSeverity,
                 @agentFilter, @alertTypeFilter, true)
            ON CONFLICT (channel_name) DO UPDATE SET
                channel_type      = EXCLUDED.channel_type,
                config            = EXCLUDED.config,
                min_severity      = EXCLUDED.min_severity,
                agent_filter      = EXCLUDED.agent_filter,
                alert_type_filter = EXCLUDED.alert_type_filter,
                is_active         = true
            """, new
        {
            channelName,
            channelType,
            configJson,
            minSeverity,
            agentFilter     = agentArr,
            alertTypeFilter = alertTypeArr,
        });

        // Invalidate the alert_channels Redis cache
        var redisDb = redis.GetDatabase();
        await redisDb.KeyDeleteAsync("alert_channels");

        logger.LogInformation("Alert channel '{Name}' ({Type}) configured", channelName, channelType);
        return Ok(new
        {
            channel_name  = channelName,
            channel_type  = channelType,
            min_severity  = minSeverity,
            active        = true,
            message       = $"Alert channel '{channelName}' saved. Cache invalidated.",
        });
    }

    private async Task<string> ConfigureAgentAlertChannelAsync(
        IReadOnlyDictionary<string, JsonElement> p, CancellationToken ct)
    {
        var channelName     = Str(p, "channel_name");
        var targetAgent     = Str(p, "target_agent");
        var minSeverity     = p.TryGetValue("min_severity", out var ms) ? ms.GetString() : "high";
        var agentFilter     = p.TryGetValue("agent_filter", out var af) ? af.GetString() : null;
        var alertTypeFilter = p.TryGetValue("alert_type_filter", out var atf) ? atf.GetString() : null;

        var configJson = JsonSerializer.Serialize(new { agent = targetAgent });

        var agentArr     = string.IsNullOrWhiteSpace(agentFilter)
            ? null : agentFilter.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        var alertTypeArr = string.IsNullOrWhiteSpace(alertTypeFilter)
            ? null : alertTypeFilter.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();

        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            INSERT INTO jarvis_schema.alert_channels
                (channel_name, channel_type, config, min_severity, agent_filter, alert_type_filter, is_active)
            VALUES
                (@channelName, 'agent', @configJson::jsonb, @minSeverity,
                 @agentFilter, @alertTypeFilter, true)
            ON CONFLICT (channel_name) DO UPDATE SET
                channel_type      = 'agent',
                config            = EXCLUDED.config,
                min_severity      = EXCLUDED.min_severity,
                agent_filter      = EXCLUDED.agent_filter,
                alert_type_filter = EXCLUDED.alert_type_filter,
                is_active         = true
            """, new
        {
            channelName,
            configJson,
            minSeverity,
            agentFilter     = agentArr,
            alertTypeFilter = alertTypeArr,
        });

        var redisDb = redis.GetDatabase();
        await redisDb.KeyDeleteAsync("alert_channels");

        logger.LogInformation("[Rocky] Agent alert channel '{Name}' → '{Agent}' configured",
            channelName, targetAgent);

        return Ok(new
        {
            channel_name = channelName,
            target_agent = targetAgent,
            min_severity = minSeverity,
            message      = $"Agent alert channel '{channelName}' configured. Alerts will be posted to agent '{targetAgent}'."
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
