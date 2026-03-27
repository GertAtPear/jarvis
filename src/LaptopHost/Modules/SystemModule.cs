using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LaptopHost.Modules;

public class SystemModule(ILogger<SystemModule> logger) : ILaptopToolModule
{
    private static readonly JsonSerializerOptions Opts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public string ModuleName => "system";

    public IEnumerable<LaptopToolSpec> GetDefinitions() =>
    [
        new("laptop_disk_report",
            "Report all mounted drives and partitions with total/used/free space (df -h). Use this to check available space on any drive or partition including secondary/external drives.",
            """{"type":"object","properties":{}}"""),

        new("laptop_dir_sizes",
            "Report top 20 largest files/directories under a path (du -sh)",
            """{"type":"object","properties":{"path":{"type":"string","description":"Root path to scan (default: home directory)"}}}"""),

        new("laptop_process_list",
            "List running processes sorted by CPU or memory usage",
            """{"type":"object","properties":{"sort_by":{"type":"string","enum":["cpu","memory","name"],"default":"cpu"},"limit":{"type":"integer","default":20}}}"""),

        new("laptop_memory_usage",
            "Report RAM total, used, and free on the laptop",
            """{"type":"object","properties":{}}"""),

        new("laptop_empty_trash",
            "Empty the system trash/recycle bin",
            """{"type":"object","properties":{}}""",
            RequireConfirm: true),

        new("laptop_find_large_files",
            "Find files above a minimum size threshold under a path",
            """{"type":"object","properties":{"path":{"type":"string"},"min_mb":{"type":"number","default":100}},"required":["path"]}""")
    ];

    public async Task<string> ExecuteAsync(string toolName, JsonDocument parameters, CancellationToken ct = default)
    {
        try
        {
            var root = parameters.RootElement;
            return toolName switch
            {
                "laptop_disk_report"      => await DiskReportAsync(ct),
                "laptop_dir_sizes"        => await DirSizesAsync(root, ct),
                "laptop_process_list"     => await ProcessListAsync(root, ct),
                "laptop_memory_usage"     => MemoryUsage(),
                "laptop_empty_trash"      => await EmptyTrashAsync(ct),
                "laptop_find_large_files" => await FindLargeFilesAsync(root, ct),
                _ => Err($"Unknown tool: {toolName}")
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[System] Tool '{Tool}' failed", toolName);
            return Err(ex.Message);
        }
    }

    private static async Task<string> DiskReportAsync(CancellationToken ct)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var output = await RunCommandAsync("df", "-h --output=source,fstype,size,used,avail,pcent,target 2>/dev/null || df -h", ct);
            return Ok(new { output = output.Trim() });
        }

        // Windows fallback: DriveInfo
        var drives = DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => new
            {
                name          = d.Name,
                type          = d.DriveType.ToString(),
                total_gb      = Math.Round(d.TotalSize / 1e9, 1),
                free_gb       = Math.Round(d.AvailableFreeSpace / 1e9, 1),
                used_gb       = Math.Round((d.TotalSize - d.AvailableFreeSpace) / 1e9, 1),
                percent_used  = (int)((d.TotalSize - d.AvailableFreeSpace) * 100.0 / d.TotalSize)
            }).ToList();
        return Ok(new { drives });
    }

    private static async Task<string> DirSizesAsync(JsonElement p, CancellationToken ct)
    {
        var path = p.TryGetProperty("path", out var pe) && !string.IsNullOrWhiteSpace(pe.GetString())
            ? Expand(pe.GetString()!)
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var output = await RunCommandAsync("du", $"-sh \"{path}\"/* 2>/dev/null | sort -rh | head -20", ct);
            return Ok(new { path, output = output.Trim() });
        }

        var entries = Directory.GetFileSystemEntries(path)
            .Select(e => new
            {
                name       = Path.GetFileName(e),
                size_bytes = Directory.Exists(e) ? GetDirectorySize(e) : new FileInfo(e).Length
            })
            .OrderByDescending(e => e.size_bytes)
            .Take(20)
            .ToList();
        return Ok(new { path, entries });
    }

    private static async Task<string> ProcessListAsync(JsonElement p, CancellationToken ct)
    {
        var sortBy = p.TryGetProperty("sort_by", out var s) ? s.GetString() ?? "cpu" : "cpu";
        var limit  = p.TryGetProperty("limit", out var l) ? l.GetInt32() : 20;

        var procs = Process.GetProcesses()
            .Select(proc =>
            {
                try
                {
                    return new
                    {
                        pid    = proc.Id,
                        name   = proc.ProcessName,
                        memory_mb = Math.Round(proc.WorkingSet64 / 1_048_576.0, 1)
                    };
                }
                catch { return null; }
            })
            .Where(p => p is not null)
            .OrderByDescending(p => p!.memory_mb)
            .Take(limit)
            .ToList();

        return Ok(new { sort_by = sortBy, count = procs.Count, processes = procs });
    }

    private static string MemoryUsage()
    {
        if (OperatingSystem.IsLinux())
        {
            // Read /proc/meminfo
            var meminfo = File.ReadAllLines("/proc/meminfo");
            long GetKb(string key)
            {
                var line = meminfo.FirstOrDefault(l => l.StartsWith(key));
                return line is null ? 0 : long.Parse(line.Split(':')[1].Trim().Split(' ')[0]);
            }

            var totalKb     = GetKb("MemTotal");
            var freeKb      = GetKb("MemFree");
            var availableKb = GetKb("MemAvailable");
            var usedKb      = totalKb - availableKb;

            return Ok(new
            {
                total_gb     = Math.Round(totalKb / 1_048_576.0, 2),
                used_gb      = Math.Round(usedKb / 1_048_576.0, 2),
                available_gb = Math.Round(availableKb / 1_048_576.0, 2),
                used_pct     = Math.Round((double)usedKb / totalKb * 100, 1)
            });
        }

        // Fallback using GC info
        var total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var used  = Environment.WorkingSet;
        return Ok(new
        {
            total_gb = Math.Round(total / 1_073_741_824.0, 2),
            used_gb  = Math.Round(used / 1_073_741_824.0, 2)
        });
    }

    private static async Task<string> EmptyTrashAsync(CancellationToken ct)
    {
        if (OperatingSystem.IsLinux())
        {
            var trashPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local/share/Trash/files");
            if (Directory.Exists(trashPath))
            {
                var count = Directory.GetFileSystemEntries(trashPath).Length;
                foreach (var entry in Directory.GetFileSystemEntries(trashPath))
                {
                    if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
                    else File.Delete(entry);
                }
                return Ok(new { emptied = true, items_deleted = count });
            }
            return Ok(new { emptied = true, items_deleted = 0, note = "Trash was already empty" });
        }

        return Err("Empty trash not implemented for this platform");
    }

    private static async Task<string> FindLargeFilesAsync(JsonElement p, CancellationToken ct)
    {
        var path   = Expand(p.GetProperty("path").GetString()!);
        var minMb  = p.TryGetProperty("min_mb", out var m) ? m.GetDouble() : 100.0;
        var minBytes = (long)(minMb * 1_048_576);

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var output = await RunCommandAsync("find",
                $"{path} -type f -size +{(long)minMb}M -exec ls -lh {{}} \\; 2>/dev/null | sort -rk5 | head -20",
                ct);
            return Ok(new { path, min_mb = minMb, output = output.Trim() });
        }

        var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories)
            .Select(f =>
            {
                try
                {
                    var fi = new FileInfo(f);
                    return fi.Length >= minBytes ? new { path = f, size_mb = Math.Round(fi.Length / 1_048_576.0, 1) } : null;
                }
                catch { return null; }
            })
            .Where(f => f is not null)
            .OrderByDescending(f => f!.size_mb)
            .Take(20)
            .ToList();

        return Ok(new { path, min_mb = minMb, files });
    }

    private static long GetDirectorySize(string path)
    {
        try
        {
            return Directory.GetFiles(path, "*", SearchOption.AllDirectories).Sum(f =>
            {
                try { return new FileInfo(f).Length; } catch { return 0; }
            });
        }
        catch { return 0; }
    }

    private static async Task<string> RunCommandAsync(string cmd, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("/bin/sh", $"-c \"{cmd} {args}\"")
        {
            RedirectStandardOutput = true,
            UseShellExecute        = false
        };
        using var proc = Process.Start(psi)!;
        var output = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return output;
    }

    private static string Expand(string path) =>
        path.StartsWith("~/") ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            path[2..]) : path;

    private static string Ok(object value)  => JsonSerializer.Serialize(value, Opts);
    private static string Err(string msg)   => JsonSerializer.Serialize(new { error = msg }, Opts);
}
