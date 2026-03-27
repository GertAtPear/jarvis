using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LaptopHost.Modules;

public class FileSystemModule(ILogger<FileSystemModule> logger) : ILaptopToolModule
{
    private static readonly JsonSerializerOptions Opts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public string ModuleName => "filesystem";

    public IEnumerable<LaptopToolSpec> GetDefinitions() =>
    [
        new("laptop_read_file",      "Read the contents of a file on the laptop",
            """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}"""),

        new("laptop_write_file",     "Write content to a file on the laptop (creates or overwrites)",
            """{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"}},"required":["path","content"]}""",
            RequireConfirm: true),

        new("laptop_append_file",    "Append content to a file on the laptop",
            """{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"}},"required":["path","content"]}"""),

        new("laptop_list_directory", "List files and directories at a path",
            """{"type":"object","properties":{"path":{"type":"string"},"recursive":{"type":"boolean"}},"required":["path"]}"""),

        new("laptop_file_exists",    "Check if a file or directory exists",
            """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}"""),

        new("laptop_delete_file",    "Delete a file from the laptop",
            """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""",
            RequireConfirm: true),

        new("laptop_disk_usage",     "Get disk usage summary for a path or the whole system",
            """{"type":"object","properties":{"path":{"type":"string"}}}""")
    ];

    public async Task<string> ExecuteAsync(string toolName, JsonDocument parameters, CancellationToken ct = default)
    {
        try
        {
            var root = parameters.RootElement;
            return toolName switch
            {
                "laptop_read_file"      => await ReadFileAsync(root),
                "laptop_write_file"     => await WriteFileAsync(root, overwrite: true),
                "laptop_append_file"    => await AppendFileAsync(root),
                "laptop_list_directory" => await ListDirectoryAsync(root),
                "laptop_file_exists"    => FileExistsAsync(root),
                "laptop_delete_file"    => await DeleteFileAsync(root),
                "laptop_disk_usage"     => DiskUsageAsync(root),
                _ => Err($"Unknown tool: {toolName}")
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[FileSystem] Tool '{Tool}' failed", toolName);
            return Err(ex.Message);
        }
    }

    private static async Task<string> ReadFileAsync(JsonElement p)
    {
        var path = Expand(p.GetProperty("path").GetString()!);
        if (!File.Exists(path))
            return Err($"File not found: {path}");
        var content = await File.ReadAllTextAsync(path);
        return Ok(new { path, size_bytes = content.Length, content });
    }

    private static async Task<string> WriteFileAsync(JsonElement p, bool overwrite)
    {
        var path    = Expand(p.GetProperty("path").GetString()!);
        var content = p.GetProperty("content").GetString()!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
        return Ok(new { path, bytes_written = content.Length });
    }

    private static async Task<string> AppendFileAsync(JsonElement p)
    {
        var path    = Expand(p.GetProperty("path").GetString()!);
        var content = p.GetProperty("content").GetString()!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.AppendAllTextAsync(path, content);
        return Ok(new { path, bytes_appended = content.Length });
    }

    private static Task<string> ListDirectoryAsync(JsonElement p)
    {
        var path      = Expand(p.GetProperty("path").GetString()!);
        var recursive = p.TryGetProperty("recursive", out var r) && r.GetBoolean();

        if (!Directory.Exists(path))
            return Task.FromResult(Err($"Directory not found: {path}"));

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var entries = Directory.GetFileSystemEntries(path, "*", option)
            .Select(e => new
            {
                name       = Path.GetFileName(e),
                full_path  = e,
                is_dir     = Directory.Exists(e),
                size_bytes = Directory.Exists(e) ? (long?)null : new FileInfo(e).Length
            }).ToList();

        return Task.FromResult(Ok(new { path, count = entries.Count, entries }));
    }

    private static string FileExistsAsync(JsonElement p)
    {
        var path   = Expand(p.GetProperty("path").GetString()!);
        var exists = File.Exists(path) || Directory.Exists(path);
        return Ok(new { path, exists, is_file = File.Exists(path), is_directory = Directory.Exists(path) });
    }

    private static async Task<string> DeleteFileAsync(JsonElement p)
    {
        var path = Expand(p.GetProperty("path").GetString()!);
        if (!File.Exists(path))
            return Err($"File not found: {path}");
        File.Delete(path);
        return Ok(new { path, deleted = true });
    }

    private static string DiskUsageAsync(JsonElement p)
    {
        if (p.TryGetProperty("path", out var pathEl) && !string.IsNullOrWhiteSpace(pathEl.GetString()))
        {
            var path = Expand(pathEl.GetString()!);
            var drive = new DriveInfo(path);
            return Ok(new
            {
                path,
                total_gb    = Math.Round(drive.TotalSize / 1_073_741_824.0, 2),
                free_gb     = Math.Round(drive.AvailableFreeSpace / 1_073_741_824.0, 2),
                used_gb     = Math.Round((drive.TotalSize - drive.AvailableFreeSpace) / 1_073_741_824.0, 2),
                used_pct    = Math.Round((double)(drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize * 100, 1)
            });
        }

        var drives = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => new
        {
            mount       = d.RootDirectory.FullName,
            total_gb    = Math.Round(d.TotalSize / 1_073_741_824.0, 2),
            free_gb     = Math.Round(d.AvailableFreeSpace / 1_073_741_824.0, 2),
            used_pct    = Math.Round((double)(d.TotalSize - d.AvailableFreeSpace) / d.TotalSize * 100, 1)
        }).ToList();

        return Ok(new { drives });
    }

    private static string Expand(string path) =>
        path.StartsWith("~/") ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            path[2..]) : path;

    private static string Ok(object value)  => JsonSerializer.Serialize(value, Opts);
    private static string Err(string msg)   => JsonSerializer.Serialize(new { error = msg }, Opts);
}
