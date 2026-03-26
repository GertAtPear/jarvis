using System.Text.Json;
using Andrew.Agent.Data;
using Andrew.Agent.Data.Repositories;
using Andrew.Agent.Models;
using Andrew.Agent.Services;
using Mediahost.Agents;
using Mediahost.Agents.Services;
using Mediahost.Agents.Tools;
using Mediahost.Llm.Models;
using Mediahost.Shared.Services;

namespace Andrew.Agent.Tools;

public class AndrewToolExecutor(
    ServerRepository servers,
    ContainerRepository containers,
    ApplicationRepository applications,
    DiscoveryLogRepository discoveryLogs,
    NetworkFactRepository networkFacts,
    ScheduledCheckRepository scheduledChecks,
    ISshDiscoveryService discoveryService,
    IWindowsDiscoveryService windowsDiscovery,
    JobSchedulerService scheduler,
    IScopedVaultService vault,
    AndrewMemoryService memory,
    IEnumerable<IToolModule> modules,
    ILogger<AndrewToolExecutor> logger) : ModularToolExecutor(modules)
{
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromHours(4);

    private static readonly JsonSerializerOptions SerializerOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    protected override IEnumerable<ToolDefinition> GetAgentSpecificDefinitions() =>
        AndrewToolDefinitions.GetTools();

    protected override async Task<string> HandleAgentSpecificAsync(
        string toolName, JsonDocument input, CancellationToken ct)
    {
        try
        {
            return toolName switch
            {
                "list_servers"       => await ListServersAsync(input, ct),
                "get_server_details" => await GetServerDetailsAsync(input, ct),
                "list_containers"    => await ListContainersAsync(input, ct),
                "find_application"   => await FindApplicationAsync(input, ct),
                "get_network_status" => await GetNetworkStatusAsync(ct),
                "get_discovery_log"  => await GetDiscoveryLogAsync(input, ct),
                "refresh_server"     => await RefreshServerAsync(input, ct),
                "register_server"       => await RegisterServerAsync(input, ct),
                "activate_server"       => await ActivateServerAsync(input, ct),
                "schedule_check"        => await ScheduleCheckAsync(input, ct),
                "list_scheduled_checks" => await ListScheduledChecksAsync(input, ct),
                "delete_scheduled_check"=> await DeleteScheduledCheckAsync(input, ct),
                "get_check_history"     => await GetCheckHistoryAsync(input, ct),
                "store_secret"          => await StoreSecretAsync(input, ct),
                "remember_fact"         => await RememberFactAsync(input, ct),
                "forget_fact"           => await ForgetFactAsync(input, ct),
                _ => Err($"Unknown tool: {toolName}")
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tool execution failed: {Tool}", toolName);
            return Err(ex.Message);
        }
    }

    // -------------------------------------------------------------------------

    private async Task<string> ListServersAsync(JsonDocument input, CancellationToken ct)
    {
        var filterStatus = input.RootElement.TryGetProperty("filter_status", out var fs)
            ? fs.GetString() : null;

        var list = string.IsNullOrEmpty(filterStatus)
            ? await servers.GetAllAsync()
            : await servers.GetByStatusAsync(filterStatus);

        var rows = list.Select(s => new
        {
            hostname = s.Hostname,
            ip_address = s.IpAddress,
            status = s.Status,
            os = s.OsName is not null ? $"{s.OsName} {s.OsVersion}".TrimEnd() : null,
            cpu_cores = s.CpuCores,
            ram_gb = s.RamGb,
            disk_total_gb = s.DiskTotalGb,
            disk_used_gb = s.DiskUsedGb,
            last_scanned_at = s.LastScannedAt,
            ssh_port = s.SshPort,
            data_age_warning = DataAgeWarning(s.LastScannedAt)
        }).ToList();

        return Ok(new { count = rows.Count, servers = rows });
    }

    private async Task<string> GetServerDetailsAsync(JsonDocument input, CancellationToken ct)
    {
        var hostname = RequireString(input, "hostname");
        var server = await servers.GetByHostnameAsync(hostname);
        if (server is null)
            return Err($"Server '{hostname}' not found in knowledge store.");

        var containerList = await containers.GetByServerIdAsync(server.Id);
        var appList = await applications.GetByServerIdAsync(server.Id);
        var recentLog = await discoveryLogs.GetRecentAsync(5);

        return Ok(new
        {
            server = new
            {
                server.Hostname,
                server.IpAddress,
                server.Status,
                server.OsName,
                server.OsVersion,
                server.CpuCores,
                server.RamGb,
                server.DiskTotalGb,
                server.DiskUsedGb,
                server.SshPort,
                server.LastScannedAt,
                server.LastSeenAt,
                server.Notes,
                vault_secret_path = server.VaultSecretPath
            },
            containers = containerList.Select(c => new
            {
                c.Name,
                c.Image,
                c.Status,
                c.ComposeProject,
                c.ComposeService,
                c.ScannedAt
            }),
            applications = appList.Select(a => new
            {
                a.Name,
                a.AppType,
                a.Framework,
                a.Port,
                a.GitRepoUrl,
                a.LastSeenRunningAt
            }),
            recent_discovery_log = recentLog,
            data_age_warning = DataAgeWarning(server.LastScannedAt)
        });
    }

    private async Task<string> ListContainersAsync(JsonDocument input, CancellationToken ct)
    {
        var hostname = input.RootElement.TryGetProperty("server_hostname", out var sh)
            ? sh.GetString() : null;
        var statusFilter = input.RootElement.TryGetProperty("status_filter", out var sf)
            ? sf.GetString() ?? "running" : "running";

        IEnumerable<ContainerInfo> list;
        if (!string.IsNullOrEmpty(hostname))
        {
            var server = await servers.GetByHostnameAsync(hostname);
            if (server is null)
                return Err($"Server '{hostname}' not found.");
            list = await containers.GetByServerIdAsync(server.Id);
        }
        else
        {
            list = statusFilter == "running"
                ? await containers.GetRunningAcrossAllServersAsync()
                : await GetAllContainersAsync();
        }

        if (statusFilter == "running")
            list = list.Where(c => c.Status?.Contains("running", StringComparison.OrdinalIgnoreCase) == true);
        else if (statusFilter == "stopped")
            list = list.Where(c => c.Status?.Contains("running", StringComparison.OrdinalIgnoreCase) != true);

        var rows = list.Select(c => new
        {
            c.Name,
            c.Image,
            c.Status,
            c.ComposeProject,
            c.ComposeService,
            c.ScannedAt
        }).ToList();

        return Ok(new { count = rows.Count, containers = rows });
    }

    private async Task<string> FindApplicationAsync(JsonDocument input, CancellationToken ct)
    {
        var term = RequireString(input, "search_term");

        var apps = await applications.SearchByNameAsync(term);
        var appList = apps.ToList();

        // Also search containers by name/image
        var running = await containers.GetRunningAcrossAllServersAsync();
        var matchedContainers = running
            .Where(c =>
                (c.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) == true) ||
                (c.Image?.Contains(term, StringComparison.OrdinalIgnoreCase) == true))
            .Select(c => new { source = "container", c.Name, c.Image, c.Status, c.ComposeProject, c.ScannedAt })
            .ToList();

        return Ok(new
        {
            search_term = term,
            app_matches = appList.Select(a => new
            {
                source = "application",
                a.Name,
                a.AppType,
                a.Framework,
                a.Port,
                a.GitRepoUrl,
                a.LastSeenRunningAt
            }),
            container_matches = matchedContainers,
            total_matches = appList.Count + matchedContainers.Count
        });
    }

    private async Task<string> GetNetworkStatusAsync(CancellationToken ct)
    {
        var facts = await networkFacts.GetAllFactsAsync();
        return Ok(new
        {
            fact_count = facts.Count,
            facts = facts.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.RootElement)
        });
    }

    private async Task<string> GetDiscoveryLogAsync(JsonDocument input, CancellationToken ct)
    {
        var hostname = input.RootElement.TryGetProperty("server_hostname", out var sh)
            ? sh.GetString() : null;
        var limit = input.RootElement.TryGetProperty("limit", out var lim)
            ? lim.GetInt32() : 10;

        var log = await discoveryLogs.GetRecentAsync(limit);
        var entries = log.ToList();

        if (!string.IsNullOrEmpty(hostname))
            entries = entries
                .Where(e => ((string?)((dynamic)e).hostname ?? "")
                    .Equals(hostname, StringComparison.OrdinalIgnoreCase))
                .ToList();

        return Ok(new { count = entries.Count, log = entries });
    }

    private async Task<string> RefreshServerAsync(JsonDocument input, CancellationToken ct)
    {
        var hostname = RequireString(input, "hostname");
        var server = await servers.GetByHostnameAsync(hostname);
        if (server is null)
            return Err($"Server '{hostname}' not found. Use register_server first.");

        var result = await DiscoverServerAsync(server, ct);
        if (!result.Success)
            return Err($"Discovery failed: {result.ErrorMessage}");

        return Ok(new
        {
            hostname,
            success = true,
            container_count = result.Containers.Count,
            application_count = result.Applications.Count,
            duration_ms = result.DurationMs
        });
    }

    private async Task<string> RegisterServerAsync(JsonDocument input, CancellationToken ct)
    {
        var hostname       = RequireString(input, "hostname");
        var ipAddress      = RequireString(input, "ip_address");
        var sshPort        = input.RootElement.TryGetProperty("ssh_port",        out var sp) ? sp.GetInt32() : 22;
        var connectionType = input.RootElement.TryGetProperty("connection_type", out var ctn) ? ctn.GetString() ?? "ssh" : "ssh";
        var notes          = input.RootElement.TryGetProperty("notes",           out var n)  ? n.GetString() : null;

        var existing = await servers.GetByHostnameAsync(hostname);
        if (existing is not null)
            return Err($"Server '{hostname}' is already registered (status: {existing.Status}).");

        var server = new ServerInfo
        {
            Hostname        = hostname,
            IpAddress       = ipAddress,
            SshPort         = sshPort,
            Status          = "pending_credentials",
            VaultSecretPath = $"/servers/{hostname}",
            Notes           = notes
        };
        ServerTagHelper.ApplyConnectionType(server, connectionType);

        await servers.UpsertAsync(server);

        var isWindows = connectionType == "winrm";
        var credInstructions = isWindows
            ? $"Add winrm_user and winrm_password at /servers/{hostname} in Infisical."
            : $"Add ssh_user and ssh_password (or ssh_key_path) at /servers/{hostname} in Infisical.";

        return Ok(new
        {
            hostname,
            connection_type   = connectionType,
            status            = "pending_credentials",
            vault_secret_path = $"/servers/{hostname}",
            message           = $"Server registered as {connectionType.ToUpper()}. {credInstructions} Then call activate_server."
        });
    }

    private async Task<string> ActivateServerAsync(JsonDocument input, CancellationToken ct)
    {
        var hostname = RequireString(input, "hostname");
        var server = await servers.GetByHostnameAsync(hostname);
        if (server is null)
            return Err($"Server '{hostname}' not found. Use register_server first.");

        var result = await DiscoverServerAsync(server, ct);
        if (!result.Success)
            return Err($"Activation failed: {result.ErrorMessage}. " +
                       $"Ensure credentials are set at /servers/{hostname} in Infisical.");

        return Ok(new
        {
            hostname,
            activated         = true,
            connection_type   = ServerTagHelper.GetConnectionType(server),
            os                = result.Server?.OsName,
            container_count   = result.Containers.Count,
            application_count = result.Applications.Count,
            duration_ms       = result.DurationMs,
            message           = $"Server '{hostname}' is now online and monitored."
        });
    }

    /// <summary>Routes discovery to the correct service based on the server's connection_type tag.</summary>
    private Task<DiscoveryResult> DiscoverServerAsync(ServerInfo server, CancellationToken ct) =>
        ServerTagHelper.GetConnectionType(server) == "winrm"
            ? windowsDiscovery.DiscoverServerAsync(server, ct)
            : discoveryService.DiscoverServerAsync(server, ct);

    // ── Scheduled checks ──────────────────────────────────────────────────────

    private async Task<string> ScheduleCheckAsync(JsonDocument input, CancellationToken ct)
    {
        var name       = RequireString(input, "name");
        var checkType  = RequireString(input, "check_type");
        var target     = RequireString(input, "target");
        var schedType  = RequireString(input, "schedule_type");

        if (checkType is not ("container_running" or "server_up" or "website_up" or "port_listening"))
            return Err($"Invalid check_type '{checkType}'. Use: container_running, server_up, website_up, port_listening");

        if (schedType is not ("interval" or "cron"))
            return Err("schedule_type must be 'interval' or 'cron'");

        int? intervalMinutes = null;
        string? cronExpression = null;

        if (schedType == "interval")
        {
            if (!input.RootElement.TryGetProperty("interval_minutes", out var im))
                return Err("interval_minutes is required for schedule_type=interval");
            intervalMinutes = im.GetInt32();
            if (intervalMinutes < 1)
                return Err("interval_minutes must be at least 1");
        }
        else
        {
            if (!input.RootElement.TryGetProperty("cron_expression", out var ce))
                return Err("cron_expression is required for schedule_type=cron");
            cronExpression = ce.GetString();
            if (string.IsNullOrEmpty(cronExpression))
                return Err("cron_expression must not be empty");
        }

        // Resolve server_id if server_hostname provided
        Guid? serverId = null;
        if (input.RootElement.TryGetProperty("server_hostname", out var sh) && sh.GetString() is { } hostname)
        {
            var srv = await servers.GetByHostnameAsync(hostname);
            if (srv is null)
                return Err($"Server '{hostname}' not found. Register it first.");
            serverId = srv.Id;
        }

        var notifyOnFailure = !input.RootElement.TryGetProperty("notify_on_failure", out var nof)
            || nof.GetBoolean();

        // Check for name collision
        var existing = await scheduledChecks.GetByNameAsync(name);
        if (existing is not null)
            return Err($"A check named '{name}' already exists (id: {existing.Id}). Delete it first or use a different name.");

        var check = new ScheduledCheck
        {
            Name             = name,
            CheckType        = checkType,
            Target           = target,
            ServerId         = serverId,
            ScheduleType     = schedType,
            IntervalMinutes  = intervalMinutes,
            CronExpression   = cronExpression,
            IsActive         = true,
            NotifyOnFailure  = notifyOnFailure
        };

        var id = await scheduledChecks.CreateAsync(check);
        var created = (await scheduledChecks.GetByIdAsync(id))!;
        await scheduler.ScheduleCheckAsync(created, ct);

        return Ok(new
        {
            id,
            name,
            check_type  = checkType,
            target,
            schedule    = created.ScheduleSummary,
            message     = $"Check '{name}' scheduled. First run: {(schedType == "interval" ? $"within {intervalMinutes}min" : "per cron schedule")}."
        });
    }

    private async Task<string> ListScheduledChecksAsync(JsonDocument input, CancellationToken ct)
    {
        var statusFilter = input.RootElement.TryGetProperty("status_filter", out var sf)
            ? sf.GetString() : "all";

        var allChecks = (await scheduledChecks.GetAllAsync()).ToList();

        if (statusFilter is not (null or "all"))
            allChecks = allChecks
                .Where(c => string.Equals(c.LastStatus ?? "unknown", statusFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        var rows = allChecks.Select(c => new
        {
            id            = c.Id,
            name          = c.Name,
            check_type    = c.CheckType,
            target        = c.Target,
            schedule      = c.ScheduleSummary,
            is_active     = c.IsActive,
            last_status   = c.LastStatus ?? "never run",
            last_checked  = c.LastCheckedAt,
            last_result   = c.LastResultJson
        }).ToList();

        return Ok(new { count = rows.Count, checks = rows });
    }

    private async Task<string> DeleteScheduledCheckAsync(JsonDocument input, CancellationToken ct)
    {
        var nameOrId = RequireString(input, "name_or_id");

        ScheduledCheck? check = Guid.TryParse(nameOrId, out var id)
            ? await scheduledChecks.GetByIdAsync(id)
            : await scheduledChecks.GetByNameAsync(nameOrId);

        if (check is null)
            return Err($"No scheduled check found matching '{nameOrId}'");

        await scheduler.UnscheduleCheckAsync(check.Id, ct);
        await scheduledChecks.DeleteAsync(check.Id);

        return Ok(new { deleted = true, name = check.Name, id = check.Id });
    }

    private async Task<string> GetCheckHistoryAsync(JsonDocument input, CancellationToken ct)
    {
        var nameOrId = RequireString(input, "name_or_id");
        var limit = input.RootElement.TryGetProperty("limit", out var lim) ? lim.GetInt32() : 20;

        ScheduledCheck? check = Guid.TryParse(nameOrId, out var id)
            ? await scheduledChecks.GetByIdAsync(id)
            : await scheduledChecks.GetByNameAsync(nameOrId);

        if (check is null)
            return Err($"No scheduled check found matching '{nameOrId}'");

        var history = await scheduledChecks.GetRecentResultsAsync(check.Id, limit);

        return Ok(new
        {
            check_name  = check.Name,
            check_type  = check.CheckType,
            target      = check.Target,
            schedule    = check.ScheduleSummary,
            last_status = check.LastStatus,
            result_count = history.Count(),
            results     = history.Select(r => new
            {
                status      = r.Status,
                duration_ms = r.DurationMs,
                checked_at  = r.CheckedAt,
                details     = r.DetailsJson
            })
        });
    }

    // ── Vault ─────────────────────────────────────────────────────────────────

    private async Task<string> StoreSecretAsync(JsonDocument input, CancellationToken ct)
    {
        var path  = RequireString(input, "path");
        var key   = RequireString(input, "key");
        var value = RequireString(input, "value");

        // Never log the value — path and key only
        logger.LogInformation("Vault write requested: path={Path} key={Key}", path, key);

        await vault.SetSecretAsync(path, key, value, ct);

        // If this path belongs to a registered server that is still pending_credentials,
        // check whether it now has enough credentials to be activated.
        var normalizedPath = "/" + path.TrimStart('/');
        var server = (await servers.GetAllAsync())
            .FirstOrDefault(s => string.Equals(
                s.VaultSecretPath?.TrimStart('/'),
                path.TrimStart('/'),
                StringComparison.OrdinalIgnoreCase));

        string? serverActivated = null;
        if (server is { Status: "pending_credentials" })
        {
            bool credentialsReady;
            if (ServerTagHelper.GetConnectionType(server) == "winrm")
            {
                var hasUser     = await vault.SecretExistsAsync(normalizedPath, "winrm_user",     ct);
                var hasPassword = await vault.SecretExistsAsync(normalizedPath, "winrm_password", ct);
                credentialsReady = hasUser && hasPassword;
            }
            else
            {
                var hasUser     = await vault.SecretExistsAsync(normalizedPath, "ssh_user",     ct);
                var hasPassword = await vault.SecretExistsAsync(normalizedPath, "ssh_password", ct);
                var hasKey      = await vault.SecretExistsAsync(normalizedPath, "ssh_key_path", ct);
                credentialsReady = hasUser && (hasPassword || hasKey);
            }

            if (credentialsReady)
            {
                await servers.UpdateStatusAsync(server.Id, "credentials_ready");
                serverActivated = server.Hostname;
                logger.LogInformation("Server {Hostname} promoted to credentials_ready", server.Hostname);
            }
        }

        return Ok(new
        {
            stored        = true,
            secret_purged = true,
            path,
            key,
            server_ready  = serverActivated is not null,
            server        = serverActivated,
            message       = serverActivated is not null
                ? $"Secret '{key}' stored. Server '{serverActivated}' now has all credentials — call activate_server to bring it online."
                : $"Secret '{key}' stored at path '{path}'. This chat exchange will be purged from history."
        });
    }

    // ── Permanent memory ──────────────────────────────────────────────────────

    private async Task<string> RememberFactAsync(JsonDocument input, CancellationToken ct)
    {
        var key   = RequireString(input, "key");
        var value = RequireString(input, "value");
        await memory.RememberFactAsync(key, value, ct);
        return Ok(new { remembered = true, key, message = $"I'll remember: {key} = {value}" });
    }

    private async Task<string> ForgetFactAsync(JsonDocument input, CancellationToken ct)
    {
        var key = RequireString(input, "key");
        await memory.ForgetFactAsync(key, ct);
        return Ok(new { forgotten = true, key });
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<IEnumerable<ContainerInfo>> GetAllContainersAsync()
    {
        var allServers = await servers.GetAllAsync();
        var result = new List<ContainerInfo>();
        foreach (var s in allServers)
            result.AddRange(await containers.GetByServerIdAsync(s.Id));
        return result;
    }

    private static string? DataAgeWarning(DateTime? lastScannedAt)
    {
        if (lastScannedAt is null) return "Never scanned";
        var age = DateTime.UtcNow - lastScannedAt.Value;
        return age > StaleThreshold
            ? $"Data is {(int)age.TotalHours}h old — consider refreshing"
            : null;
    }

    private static string RequireString(JsonDocument doc, string key)
    {
        if (!doc.RootElement.TryGetProperty(key, out var prop))
            throw new ArgumentException($"Required parameter '{key}' is missing.");
        return prop.GetString() ?? throw new ArgumentException($"Parameter '{key}' must not be null.");
    }

    private static string Ok(object value) =>
        JsonSerializer.Serialize(value, SerializerOpts);

    private static string Err(string message) =>
        JsonSerializer.Serialize(new { error = message }, SerializerOpts);
}
