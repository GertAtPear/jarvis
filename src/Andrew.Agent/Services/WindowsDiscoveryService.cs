using System.Diagnostics;
using System.Text.Json;
using Andrew.Agent.Data.Repositories;
using Andrew.Agent.Models;
using Mediahost.Agents.Services;
using Mediahost.Shared.Services;

namespace Andrew.Agent.Services;

public sealed class WindowsDiscoveryService(
    WinRmService winrm,
    IScopedVaultService vault,
    ServerRepository servers,
    ContainerRepository containers,
    DiscoveryLogRepository discoveryLogs,
    ILogger<WindowsDiscoveryService> logger) : IWindowsDiscoveryService
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public async Task<DiscoveryResult> DiscoverServerAsync(
        ServerInfo server, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // STEP 1 — Load WinRM credentials from vault
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

        secrets.TryGetValue("winrm_user",     out var winrmUser);
        secrets.TryGetValue("winrm_password", out var winrmPassword);
        secrets.TryGetValue("winrm_port",     out var winrmPortStr);

        if (string.IsNullOrWhiteSpace(winrmUser) || string.IsNullOrWhiteSpace(winrmPassword))
        {
            return new DiscoveryResult
            {
                Success = false,
                ErrorMessage = $"No WinRM credentials in vault at {vaultPath}. " +
                               "Add winrm_user and winrm_password via the Infisical UI."
            };
        }

        var winrmPort = int.TryParse(winrmPortStr, out var p) ? p : 5985;
        var host = string.IsNullOrWhiteSpace(server.IpAddress) ? server.Hostname : server.IpAddress;

        // STEP 2 — OS info
        try
        {
            var os = await RunAsync(host, winrmPort, winrmUser, winrmPassword,
                "Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,OSArchitecture | ConvertTo-Json -Compress",
                ct: ct);
            ApplyOsInfo(server, os);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[{Host}] WinRM OS info failed", server.Hostname);
        }

        // Hardware
        try
        {
            var hw = await RunAsync(host, winrmPort, winrmUser, winrmPassword,
                "Get-CimInstance Win32_ComputerSystem | Select-Object NumberOfLogicalProcessors,TotalPhysicalMemory | ConvertTo-Json -Compress",
                ct: ct);
            ApplyHardwareInfo(server, hw);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[{Host}] WinRM hardware info failed", server.Hostname);
        }

        // Disk
        try
        {
            var disk = await RunAsync(host, winrmPort, winrmUser, winrmPassword,
                "Get-PSDrive -Name C | Select-Object Used,Free | ConvertTo-Json -Compress",
                ct: ct);
            ApplyDiskInfo(server, disk);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[{Host}] WinRM disk info failed", server.Hostname);
        }

        // STEP 3 — Build container list
        var containerList = new List<ContainerInfo>();

        // Docker containers (if Docker Desktop is installed)
        try
        {
            var dockerOut = await RunAsync(host, winrmPort, winrmUser, winrmPassword,
                "docker ps -a --format '{{json .}}' 2>$null",
                ct: ct);
            var dockerContainers = ParseDockerContainers(dockerOut, server.Id);
            containerList.AddRange(dockerContainers);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[{Host}] Docker not available or failed", server.Hostname);
        }

        // Windows services
        try
        {
            var svcOut = await RunAsync(host, winrmPort, winrmUser, winrmPassword,
                "Get-Service | Where-Object {$_.Status -eq 'Running'} | Select-Object Name,DisplayName,Status | ConvertTo-Json -Depth 2 -Compress",
                ct: ct);
            containerList.AddRange(ParseWindowsServices(svcOut, server.Id));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[{Host}] WinRM services discovery failed", server.Hostname);
        }

        // Running user processes (session > 0 = interactive / user sessions)
        try
        {
            var procOut = await RunAsync(host, winrmPort, winrmUser, winrmPassword,
                "Get-Process | Where-Object {$_.SessionId -gt 0} | Sort-Object CPU -Descending | Select-Object -First 100 Id,Name,Path,CPU,WorkingSet | ConvertTo-Json -Depth 2 -Compress",
                ct: ct);
            containerList.AddRange(ParseRunningProcesses(procOut, server.Id));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[{Host}] WinRM process discovery failed", server.Hostname);
        }

        // STEP 4 — Read recent Application Event Log errors
        object? recentErrors = null;
        object? dotnetCrashes = null;

        try
        {
            var errOut = await RunAsync(host, winrmPort, winrmUser, winrmPassword,
                "Get-WinEvent -FilterHashtable @{LogName='Application';Level=2,3;StartTime=(Get-Date).AddHours(-24)} " +
                "-MaxEvents 30 -ErrorAction SilentlyContinue | " +
                "Select-Object TimeCreated,ProviderName,Id,Message | ConvertTo-Json -Depth 2 -Compress",
                ct: ct);
            if (!string.IsNullOrWhiteSpace(errOut))
                recentErrors = JsonSerializer.Deserialize<JsonElement>(errOut, JsonOpts);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[{Host}] Event log error query failed", server.Hostname);
        }

        try
        {
            var crashOut = await RunAsync(host, winrmPort, winrmUser, winrmPassword,
                "Get-WinEvent -FilterHashtable @{LogName='Application';Id=1000,1026;StartTime=(Get-Date).AddDays(-7)} " +
                "-MaxEvents 10 -ErrorAction SilentlyContinue | " +
                "Select-Object TimeCreated,ProviderName,Id,Message | ConvertTo-Json -Depth 2 -Compress",
                ct: ct);
            if (!string.IsNullOrWhiteSpace(crashOut))
                dotnetCrashes = JsonSerializer.Deserialize<JsonElement>(crashOut, JsonOpts);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[{Host}] .NET crash query failed", server.Hostname);
        }

        // STEP 5 — Persist
        server.Status      = "online";
        server.LastScannedAt = DateTime.UtcNow;
        server.LastSeenAt    = DateTime.UtcNow;
        server.UpdatedAt     = DateTime.UtcNow;

        await servers.UpsertAsync(server);
        await containers.BulkUpsertAsync(server.Id, containerList);

        sw.Stop();
        await discoveryLogs.LogAsync(server.Id, "full", true,
            new
            {
                serviceCount   = containerList.Count(c => c.ComposeProject == "windows_services"),
                processCount   = containerList.Count(c => c.ComposeProject == "windows_apps"),
                dockerCount    = containerList.Count(c => c.ComposeProject is null),
                recentErrors,
                dotnetCrashes
            },
            (int)sw.ElapsedMilliseconds);

        return new DiscoveryResult
        {
            Success    = true,
            Server     = server,
            Containers = containerList,
            DurationMs = (int)sw.ElapsedMilliseconds
        };
    }

    // ── WinRM helper ──────────────────────────────────────────────────────────

    private async Task<string> RunAsync(
        string host, int port, string user, string password,
        string command, int timeout = 30, CancellationToken ct = default)
    {
        var result = await winrm.ExecuteAsync(host, port, user, password, command, timeout, ct);
        return result.Stdout;
    }

    // ── OS / hardware parsers ─────────────────────────────────────────────────

    private static void ApplyOsInfo(ServerInfo server, string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("Caption",  out var cap)) server.OsName    = cap.GetString();
        if (root.TryGetProperty("Version",  out var ver)) server.OsVersion = ver.GetString();
    }

    private static void ApplyHardwareInfo(ServerInfo server, string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("NumberOfLogicalProcessors", out var cpu))
            server.CpuCores = cpu.TryGetInt32(out var c) ? c : null;

        if (root.TryGetProperty("TotalPhysicalMemory", out var ram) && ram.TryGetInt64(out var bytes))
            server.RamGb = Math.Round(bytes / 1073741824m, 1);  // bytes → GB
    }

    private static void ApplyDiskInfo(ServerInfo server, string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("Used", out var used) && used.TryGetInt64(out var usedBytes) &&
            root.TryGetProperty("Free", out var free) && free.TryGetInt64(out var freeBytes))
        {
            server.DiskTotalGb = Math.Round((usedBytes + freeBytes) / 1073741824m, 1);
            server.DiskUsedGb  = Math.Round(usedBytes / 1073741824m, 1);
        }
    }

    // ── Docker container parser (same format as Linux docker ps --format '{{json .}}') ──

    private static List<ContainerInfo> ParseDockerContainers(string output, Guid serverId)
    {
        var result = new List<ContainerInfo>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('{')) continue;

            try
            {
                using var doc    = JsonDocument.Parse(trimmed);
                var root         = doc.RootElement;
                var id           = GetString(root, "ID") ?? GetString(root, "Id") ?? Guid.NewGuid().ToString("N")[..12];
                var name         = (GetString(root, "Names") ?? id).TrimStart('/').Split(',')[0].TrimStart('/');
                var image        = GetString(root, "Image");
                var state        = GetString(root, "State") ?? GetString(root, "Status");

                result.Add(new ContainerInfo
                {
                    ServerId    = serverId,
                    ContainerId = id,
                    Name        = name,
                    Image       = image,
                    Status      = state,
                    ScannedAt   = DateTime.UtcNow
                });
            }
            catch { /* skip malformed line */ }
        }
        return result;
    }

    // ── Windows service parser ────────────────────────────────────────────────

    private static List<ContainerInfo> ParseWindowsServices(string json, Guid serverId)
    {
        var result = new List<ContainerInfo>();
        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            using var doc  = JsonDocument.Parse(json);
            var root       = doc.RootElement;
            var services   = root.ValueKind == JsonValueKind.Array ? root : default;

            // PS returns an object (not array) when there is only one result
            if (root.ValueKind == JsonValueKind.Object)
                services = JsonDocument.Parse($"[{json}]").RootElement;

            if (services.ValueKind != JsonValueKind.Array) return result;

            foreach (var svc in services.EnumerateArray())
            {
                var name        = GetString(svc, "Name")        ?? "";
                var displayName = GetString(svc, "DisplayName") ?? name;
                if (string.IsNullOrWhiteSpace(name)) continue;

                result.Add(new ContainerInfo
                {
                    ServerId    = serverId,
                    ContainerId = $"svc:{name}",
                    Name        = displayName,
                    Image       = "windows-service",
                    Status      = "running",
                    ComposeProject = "windows_services",
                    ScannedAt   = DateTime.UtcNow
                });
            }
        }
        catch { /* best effort */ }

        return result;
    }

    // ── Running process parser ────────────────────────────────────────────────

    private static List<ContainerInfo> ParseRunningProcesses(string json, Guid serverId)
    {
        var result = new List<ContainerInfo>();
        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root      = doc.RootElement;

            var procs = root.ValueKind == JsonValueKind.Array ? root : default;
            if (root.ValueKind == JsonValueKind.Object)
                procs = JsonDocument.Parse($"[{json}]").RootElement;

            if (procs.ValueKind != JsonValueKind.Array) return result;

            // Deduplicate by process name — keep highest-memory instance
            var byName = new Dictionary<string, ContainerInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var proc in procs.EnumerateArray())
            {
                var name = GetString(proc, "Name") ?? "";
                if (string.IsNullOrWhiteSpace(name)) continue;

                var path   = GetString(proc, "Path");
                var cpuVal = proc.TryGetProperty("CPU",         out var cpu) && cpu.ValueKind != JsonValueKind.Null
                    ? (decimal?)cpu.GetDecimal() : null;
                var memVal = proc.TryGetProperty("WorkingSet",  out var ws)  && ws.ValueKind  != JsonValueKind.Null
                    ? (decimal?)(ws.GetInt64() / 1048576m) : null;  // bytes → MB

                if (!byName.TryGetValue(name, out var existing) ||
                    (memVal ?? 0) > (existing.MemMb ?? 0))
                {
                    var envDoc = path is not null
                        ? JsonDocument.Parse($"{{\"path\":{JsonSerializer.Serialize(path)}}}")
                        : null;

                    byName[name] = new ContainerInfo
                    {
                        ServerId       = serverId,
                        ContainerId    = $"app:{name}",
                        Name           = name,
                        Image          = path ?? "running-process",
                        Status         = "running",
                        ComposeProject = "windows_apps",
                        CpuPercent     = cpuVal.HasValue ? Math.Round(cpuVal.Value, 1) : null,
                        MemMb          = memVal.HasValue ? Math.Round(memVal.Value, 0) : null,
                        EnvVars        = envDoc,
                        ScannedAt      = DateTime.UtcNow
                    };
                }
            }

            result.AddRange(byName.Values);
        }
        catch { /* best effort */ }

        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
