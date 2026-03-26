using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Andrew.Agent.Data.Repositories;
using Andrew.Agent.Models;
using Mediahost.Shared.Services;
using Mediahost.Tools.Interfaces;
using Mediahost.Tools.Models;

namespace Andrew.Agent.Services;

public sealed partial class SshDiscoveryService(
    ISshTool sshTool,
    IScopedVaultService vault,
    ServerRepository servers,
    ContainerRepository containers,
    ApplicationRepository applications,
    DiscoveryLogRepository discoveryLogs,
    ILogger<SshDiscoveryService> logger) : ISshDiscoveryService
{
    [GeneratedRegex(@"password|secret|key|token|pwd|pass", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveKeyPattern();

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public async Task<DiscoveryResult> DiscoverServerAsync(ServerInfo server, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // STEP 1 — Fetch SSH credentials from vault
        var vaultPath = $"/servers/{server.Hostname}";
        Dictionary<string, string> secrets;
        try
        {
            secrets = await vault.GetSecretsBulkAsync(vaultPath, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read vault secrets at {Path}", vaultPath);
            secrets = [];
        }

        secrets.TryGetValue("ssh_user", out var sshUser);
        secrets.TryGetValue("ssh_key_path", out var sshKeyPath);
        secrets.TryGetValue("ssh_password", out var sshPassword);

        if (string.IsNullOrWhiteSpace(sshKeyPath) && string.IsNullOrWhiteSpace(sshPassword))
        {
            return new DiscoveryResult
            {
                Success = false,
                ErrorMessage = $"No SSH credentials in vault at {vaultPath}. " +
                               $"Add ssh_user and ssh_password (or ssh_key_path) via the Infisical UI."
            };
        }

        sshUser ??= "root";
        var sshPort = server.SshPort > 0 ? server.SshPort : 22;

        // STEP 2 — Build tool models (connection is established per-command via ISshTool)
        var target = new ConnectionTarget(server.Hostname, server.IpAddress, sshPort, OsType.Linux);
        var sshCreds = !string.IsNullOrWhiteSpace(sshKeyPath)
            ? SshCredentials.FromKeyFile(sshUser, sshKeyPath)
            : SshCredentials.FromPassword(sshUser, sshPassword!);

        {
            // STEP 3a — OS + hardware info
            try
            {
                var r = await sshTool.RunCommandAsync(target, sshCreds,
                    "uname -r && cat /etc/os-release | grep -E 'NAME|VERSION=' && " +
                    "nproc && free -m | awk '/^Mem:/{print $2}' && " +
                    "df -h / | awk 'NR==2{print $2,$3}'", ct);
                ApplyOsInfo(server, r.Value ?? "");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[{Host}] OS info discovery failed", server.Hostname);
            }

            // STEP 3b — Docker containers
            var parsed = new List<ContainerParseResult>();
            try
            {
                var r = await sshTool.RunCommandAsync(target, sshCreds,
                    "docker ps -a --format '{{json .}}' 2>/dev/null", ct);
                parsed = await ParseDockerContainersAsync(target, sshCreds, server.Id, r.Value ?? "", ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[{Host}] Docker container discovery failed", server.Hostname);
            }

            var containerList = parsed.Select(p => p.Container).ToList();

            // STEP 3c — Docker Compose projects
            try
            {
                var r = await sshTool.RunCommandAsync(target, sshCreds,
                    "docker compose ls --format json 2>/dev/null || echo '[]'", ct);
                ApplyComposeProjects(containerList, r.Value ?? "");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[{Host}] Docker Compose discovery failed", server.Hostname);
            }

            // STEP 3d — Top processes
            var processes = new List<ProcessInfo>();
            try
            {
                var r = await sshTool.RunCommandAsync(target, sshCreds,
                    "ps aux --sort=-%cpu --no-headers | head -30", ct);
                processes = ParseProcesses(r.Value ?? "");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[{Host}] Process discovery failed", server.Hostname);
            }

            // STEP 3e — Listening TCP ports
            var listeningPorts = new List<int>();
            try
            {
                var r = await sshTool.RunCommandAsync(target, sshCreds,
                    "ss -tlnp 2>/dev/null | tail -n +2", ct);
                listeningPorts = ParseListeningPorts(r.Value ?? "");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[{Host}] Port discovery failed", server.Hostname);
            }

            // STEP 3f — Systemd services
            var systemdServices = new List<string>();
            try
            {
                var r = await sshTool.RunCommandAsync(target, sshCreds,
                    "systemctl list-units --type=service --state=running --no-pager --plain 2>/dev/null | awk 'NR>1{print $1}'", ct);
                systemdServices = ParseSystemdServices(r.Value ?? "");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[{Host}] Systemd discovery failed", server.Hostname);
            }

            // STEP 4 — Persist servers and containers first so container DB UUIDs are populated
            server.Status = "online";
            server.LastScannedAt = DateTime.UtcNow;
            server.LastSeenAt = DateTime.UtcNow;
            server.UpdatedAt = DateTime.UtcNow;

            await servers.UpsertAsync(server);
            await containers.BulkUpsertAsync(server.Id, containerList);

            // STEP 5 — Infer and persist applications (container.Id now holds the DB UUID)
            var appList = parsed
                .Select(p => InferApplication(p.Container, server.Id, p.GitRepoUrl))
                .ToList();
            foreach (var app in appList)
                await applications.UpsertAsync(app);

            sw.Stop();
            await discoveryLogs.LogAsync(server.Id, "full", true,
                new
                {
                    containerCount = containerList.Count,
                    processCount = processes.Count,
                    portCount = listeningPorts.Count,
                    serviceCount = systemdServices.Count
                },
                (int)sw.ElapsedMilliseconds);

            // STEP 6 — Return
            return new DiscoveryResult
            {
                Success = true,
                Server = server,
                Containers = containerList,
                Applications = appList,
                Processes = processes,
                ListeningPorts = listeningPorts,
                SystemdServices = systemdServices,
                DurationMs = (int)sw.ElapsedMilliseconds
            };
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static void ApplyOsInfo(ServerInfo server, string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string? osName = null;
        string? osVersion = null;
        int? cpuCores = null;
        decimal? ramGb = null;
        decimal? diskTotalGb = null;

        foreach (var line in lines)
        {
            if (line.StartsWith("NAME=") && !line.StartsWith("PRETTY_NAME="))
            {
                osName = line[5..].Trim('"');
            }
            else if (line.StartsWith("VERSION=") && !line.StartsWith("VERSION_ID=") && !line.StartsWith("VERSION_CODENAME="))
            {
                osVersion = line[8..].Trim('"');
            }
            else if (!line.Contains('=') && int.TryParse(line, out var intVal) && cpuCores is null)
            {
                cpuCores = intVal;
            }
            else if (!line.Contains('=') && int.TryParse(line, out var ramMb) && cpuCores is not null && ramGb is null)
            {
                ramGb = Math.Round(ramMb / 1024m, 1);
            }
            else if (!line.Contains('=') && line.Contains(' ') && diskTotalGb is null && ramGb is not null)
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    diskTotalGb = ParseDiskSize(parts[0]);
                    var diskUsedGb = ParseDiskSize(parts[1]);
                    if (diskUsedGb is not null) server.DiskUsedGb = diskUsedGb;
                }
            }
        }

        if (osName is not null) server.OsName = osName;
        if (osVersion is not null) server.OsVersion = osVersion;
        if (cpuCores is not null) server.CpuCores = cpuCores;
        if (ramGb is not null) server.RamGb = ramGb;
        if (diskTotalGb is not null) server.DiskTotalGb = diskTotalGb;
    }

    private static decimal? ParseDiskSize(string s)
    {
        if (s.Length < 2) return null;
        var unit = s[^1];
        if (!decimal.TryParse(s[..^1], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var val))
            return null;

        return unit switch
        {
            'T' or 't' => Math.Round(val * 1024, 1),
            'G' or 'g' => Math.Round(val, 1),
            'M' or 'm' => Math.Round(val / 1024, 2),
            _ => Math.Round(val, 1)
        };
    }

    private async Task<List<ContainerParseResult>> ParseDockerContainersAsync(
        ConnectionTarget target, SshCredentials credentials, Guid serverId, string output, CancellationToken ct)
    {
        var result = new List<ContainerParseResult>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('{')) continue;

            DockerPsEntry? entry;
            try { entry = JsonSerializer.Deserialize<DockerPsEntry>(trimmed, JsonOpts); }
            catch { continue; }
            if (entry is null) continue;

            // Env vars — inspect and redact sensitive keys
            JsonDocument? envDoc = null;
            try
            {
                var envResult = await sshTool.RunCommandAsync(target, credentials,
                    $"docker inspect {entry.ID} --format '{{{{json .Config.Env}}}}'", ct);
                var envArray = JsonSerializer.Deserialize<string[]>((envResult.Value ?? "").Trim(), JsonOpts);
                if (envArray is not null)
                {
                    var dict = new Dictionary<string, string>(envArray.Length);
                    foreach (var pair in envArray)
                    {
                        var idx = pair.IndexOf('=');
                        if (idx < 0) continue;
                        var k = pair[..idx];
                        var v = pair[(idx + 1)..];
                        dict[k] = SensitiveKeyPattern().IsMatch(k) ? "***REDACTED***" : v;
                    }
                    envDoc = JsonDocument.Parse(JsonSerializer.Serialize(dict));
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not inspect env for container {Id}", entry.ID);
            }

            // Ports string → JSON array
            JsonDocument? portsDoc = null;
            if (!string.IsNullOrWhiteSpace(entry.Ports))
            {
                try
                {
                    var portsList = entry.Ports.Split(',')
                        .Select(p => p.Trim())
                        .Where(p => p.Length > 0)
                        .ToList();
                    portsDoc = JsonDocument.Parse(JsonSerializer.Serialize(portsList));
                }
                catch { /* best-effort */ }
            }

            // Labels → compose project/service + git repo URL
            string? composeProject = null;
            string? composeService = null;
            string? gitRepoUrl = null;
            if (!string.IsNullOrWhiteSpace(entry.Labels))
            {
                foreach (var pair in entry.Labels.Split(','))
                {
                    var eq = pair.IndexOf('=');
                    if (eq < 0) continue;
                    var k = pair[..eq].Trim();
                    var v = pair[(eq + 1)..].Trim();
                    switch (k)
                    {
                        case "com.docker.compose.project": composeProject = v; break;
                        case "com.docker.compose.service": composeService = v; break;
                        case "org.opencontainers.image.source": gitRepoUrl = v; break;
                    }
                }
            }

            DateTime? createdAt = null;
            if (DateTime.TryParse(entry.CreatedAt, out var dt))
                createdAt = dt.ToUniversalTime();

            var container = new ContainerInfo
            {
                Id = Guid.NewGuid(),
                ServerId = serverId,
                ContainerId = entry.ID,
                Name = entry.Names.TrimStart('/').Split(',')[0].TrimStart('/'),
                Image = entry.Image,
                Status = entry.State,
                Ports = portsDoc,
                EnvVars = envDoc,
                ComposeProject = composeProject,
                ComposeService = composeService,
                CreatedAtContainer = createdAt,
                ScannedAt = DateTime.UtcNow
            };

            result.Add(new ContainerParseResult(container, gitRepoUrl));
        }

        return result;
    }

    private static void ApplyComposeProjects(List<ContainerInfo> containerList, string composeOutput)
    {
        var trimmed = composeOutput.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed == "[]") return;

        try
        {
            var projects = JsonSerializer.Deserialize<DockerComposeProject[]>(trimmed, JsonOpts);
            if (projects is null) return;

            foreach (var project in projects)
            {
                foreach (var c in containerList.Where(c =>
                    c.ComposeProject is null &&
                    (c.Name.StartsWith(project.Name + "_", StringComparison.Ordinal) ||
                     c.Name.StartsWith(project.Name + "-", StringComparison.Ordinal))))
                {
                    c.ComposeProject = project.Name;
                }
            }
        }
        catch { /* best-effort */ }
    }

    private static List<ProcessInfo> ParseProcesses(string output)
    {
        var result = new List<ProcessInfo>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // ps aux columns: USER PID %CPU %MEM VSZ RSS TTY STAT START TIME COMMAND...
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 11) continue;
            if (!int.TryParse(parts[1], out var pid)) continue;
            if (!decimal.TryParse(parts[2], System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var cpu)) continue;
            if (!decimal.TryParse(parts[3], System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var mem)) continue;

            result.Add(new ProcessInfo
            {
                User = parts[0],
                Pid = pid,
                CpuPercent = cpu,
                MemPercent = mem,
                Command = string.Join(' ', parts[10..])
            });
        }
        return result;
    }

    private static List<int> ParseListeningPorts(string output)
    {
        var result = new HashSet<int>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // ss -tlnp columns: State Recv-Q Send-Q Local:Port Peer:Port [Process]
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) continue;

            var localAddr = parts[3];
            var lastColon = localAddr.LastIndexOf(':');
            if (lastColon < 0) continue;
            if (int.TryParse(localAddr[(lastColon + 1)..], out var port) && port > 0)
                result.Add(port);
        }
        return [.. result.Order()];
    }

    private static List<string> ParseSystemdServices(string output) =>
        [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Where(l => l.Length > 0)];

    private static ApplicationInfo InferApplication(ContainerInfo container, Guid serverId, string? gitRepoUrl)
    {
        var image = container.Image?.ToLowerInvariant() ?? "";
        var name = container.Name.ToLowerInvariant();

        var appType = image switch
        {
            _ when image.Contains("nginx") || image.Contains("traefik") || image.Contains("caddy")
                => "proxy",
            _ when image.Contains("postgres") || image.Contains("mysql") || image.Contains("mariadb")
                => "database",
            _ when image.Contains("redis") || image.Contains("memcached")
                => "cache",
            _ when image.Contains("kafka") || image.Contains("zookeeper") || image.Contains("rabbitmq")
                => "queue",
            _ when name.Contains("api")
                => "webapi",
            _ when name.Contains("worker") || name.Contains("processor") || name.Contains("stt")
                => "worker",
            _ when name.Contains("ui") || name.Contains("web") || name.Contains("front") || name.Contains("portal")
                => "webapp",
            _ when name.Contains("scheduler") || name.Contains("cron") || name.Contains("job")
                => "scheduler",
            _ => "unknown"
        };

        var framework = image switch
        {
            _ when image.Contains("dotnet") || image.Contains("aspnet") => "dotnet",
            _ when image.Contains("node") => "nodejs",
            _ when image.Contains("python") => "python",
            _ when image.Contains("java") || image.Contains("jdk") => "java",
            _ => (string?)null
        };

        return new ApplicationInfo
        {
            Name = container.Name,
            ServerId = serverId,
            ContainerId = container.Id,
            AppType = appType,
            Framework = framework,
            GitRepoUrl = gitRepoUrl,
            LastSeenRunningAt = string.Equals(container.Status, "running", StringComparison.OrdinalIgnoreCase)
                ? DateTime.UtcNow
                : null
        };
    }

    // -------------------------------------------------------------------------
    // Private DTOs
    // -------------------------------------------------------------------------

    private sealed class DockerPsEntry
    {
        [JsonPropertyName("ID")]
        public string ID { get; init; } = "";
        [JsonPropertyName("Names")]
        public string Names { get; init; } = "";
        [JsonPropertyName("Image")]
        public string Image { get; init; } = "";
        [JsonPropertyName("State")]
        public string State { get; init; } = "";
        [JsonPropertyName("Ports")]
        public string Ports { get; init; } = "";
        [JsonPropertyName("Labels")]
        public string Labels { get; init; } = "";
        [JsonPropertyName("CreatedAt")]
        public string CreatedAt { get; init; } = "";
    }

    private sealed class DockerComposeProject
    {
        [JsonPropertyName("Name")]
        public string Name { get; init; } = "";
    }

    private sealed record ContainerParseResult(ContainerInfo Container, string? GitRepoUrl);
}
