using System.Text.Json;
using Mediahost.Llm.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Mediahost.Agents.Tools;

/// <summary>
/// Shared tool module for reading/writing files in the shared agent workspace volume.
/// Agents register this module to gain access to cross-agent file exchange tools.
///
/// Register in any agent's DI setup:
///   services.AddScoped&lt;IToolModule, WorkspaceModule&gt;();
///
/// Requires configuration: Workspace:Path (default: /workspace)
/// </summary>
public class WorkspaceModule : IToolModule
{
    private readonly string _workspacePath;
    private readonly bool _available;
    private readonly ILogger<WorkspaceModule> _logger;

    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    public WorkspaceModule(IConfiguration config, ILogger<WorkspaceModule> logger)
    {
        _logger = logger;
        _workspacePath = config["Workspace:Path"] ?? "/workspace";

        try
        {
            if (!Directory.Exists(_workspacePath))
            {
                logger.LogWarning("[WorkspaceModule] Workspace directory '{Path}' does not exist — module unavailable", _workspacePath);
                _available = false;
                return;
            }

            // Writable check — create and immediately delete a probe file
            var probe = Path.Combine(_workspacePath, $".probe_{Guid.NewGuid():N}");
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
            _available = true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[WorkspaceModule] Workspace '{Path}' is not writable — module unavailable", _workspacePath);
            _available = false;
        }
    }

    public IEnumerable<ToolDefinition> GetDefinitions() =>
    [
        new ToolDefinition(
            "workspace_write_file",
            "Write text content to a file in the shared agent workspace. Other agents can read it. " +
            "Use for passing CSVs, reports, config snapshots, or any text output between agents across turns.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "filename": { "type": "string",  "description": "Relative path within workspace, e.g. 'downloads/report.csv'" },
                "content":  { "type": "string",  "description": "Text content to write (UTF-8)" },
                "overwrite": { "type": "boolean", "description": "Default true. If false and file already exists, returns an error instead of overwriting." }
              },
              "required": ["filename", "content"]
            }
            """)),

        new ToolDefinition(
            "workspace_read_file",
            "Read a file from the shared agent workspace.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "filename":  { "type": "string",  "description": "Relative path within workspace" },
                "max_chars": { "type": "integer", "description": "Max characters to return. Default 50000, max 200000." }
              },
              "required": ["filename"]
            }
            """)),

        new ToolDefinition(
            "workspace_list_files",
            "List files in the shared agent workspace or a subdirectory.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "path":    { "type": "string", "description": "Optional subdirectory to list. Defaults to workspace root." },
                "pattern": { "type": "string", "description": "Optional glob pattern, e.g. '*.csv'" }
              }
            }
            """)),

        new ToolDefinition(
            "workspace_delete_file",
            "Delete a file or empty directory from the shared agent workspace.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "filename": { "type": "string", "description": "Relative path of file or empty directory to delete" }
              },
              "required": ["filename"]
            }
            """)),

        new ToolDefinition(
            "workspace_get_info",
            "Returns workspace path, total file count, total size, and availability status.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {}
            }
            """))
    ];

    public async Task<string?> TryExecuteAsync(string toolName, JsonDocument input, CancellationToken ct = default)
    {
        return toolName switch
        {
            "workspace_write_file"  => await WriteFileAsync(input),
            "workspace_read_file"   => await ReadFileAsync(input),
            "workspace_list_files"  => await ListFilesAsync(input),
            "workspace_delete_file" => await DeleteFileAsync(input),
            "workspace_get_info"    => await GetInfoAsync(),
            _ => null   // not our tool
        };
    }

    // ── Tool implementations ──────────────────────────────────────────────────

    private Task<string> WriteFileAsync(JsonDocument input)
    {
        if (!_available)
            return Task.FromResult(Error("Workspace is unavailable — directory missing or not writable"));

        var root      = input.RootElement;
        var filename  = root.GetProperty("filename").GetString()!;
        var content   = root.GetProperty("content").GetString()!;
        var overwrite = !root.TryGetProperty("overwrite", out var ov) || ov.GetBoolean();

        var resolvedPath = ResolvePath(filename);
        if (resolvedPath is null)
            return Task.FromResult(Error("Path traversal detected — filename must resolve within the workspace"));

        if (!overwrite && File.Exists(resolvedPath))
            return Task.FromResult(Error($"File already exists at '{filename}' and overwrite is false"));

        Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath)!);
        File.WriteAllText(resolvedPath, content, System.Text.Encoding.UTF8);

        var bytes = new FileInfo(resolvedPath).Length;
        return Task.FromResult(JsonSerializer.Serialize(new { path = resolvedPath, bytes }, WriteOpts));
    }

    private Task<string> ReadFileAsync(JsonDocument input)
    {
        if (!_available)
            return Task.FromResult(Error("Workspace is unavailable — directory missing or not writable"));

        var root     = input.RootElement;
        var filename = root.GetProperty("filename").GetString()!;
        var maxChars = root.TryGetProperty("max_chars", out var mc) ? Math.Min(mc.GetInt32(), 200_000) : 50_000;

        var resolvedPath = ResolvePath(filename);
        if (resolvedPath is null)
            return Task.FromResult(Error("Path traversal detected — filename must resolve within the workspace"));

        if (!File.Exists(resolvedPath))
            return Task.FromResult(Error($"File not found: '{filename}'"));

        var text      = File.ReadAllText(resolvedPath, System.Text.Encoding.UTF8);
        var truncated = text.Length > maxChars;
        if (truncated)
            text = text[..maxChars];

        return Task.FromResult(JsonSerializer.Serialize(
            new
            {
                path      = resolvedPath,
                content   = text,
                truncated,
                note      = truncated ? $"Content truncated at {maxChars} characters" : null
            },
            WriteOpts));
    }

    private Task<string> ListFilesAsync(JsonDocument input)
    {
        if (!_available)
            return Task.FromResult(Error("Workspace is unavailable — directory missing or not writable"));

        var root    = input.RootElement;
        var subPath = root.TryGetProperty("path", out var p) ? p.GetString() : null;
        var pattern = root.TryGetProperty("pattern", out var pat) ? pat.GetString() : null;

        string searchRoot;
        if (string.IsNullOrEmpty(subPath))
        {
            searchRoot = _workspacePath;
        }
        else
        {
            var resolved = ResolvePath(subPath);
            if (resolved is null)
                return Task.FromResult(Error("Path traversal detected"));
            searchRoot = resolved;
        }

        if (!Directory.Exists(searchRoot))
            return Task.FromResult(Error($"Directory not found: '{subPath}'"));

        var searchPattern = string.IsNullOrEmpty(pattern) ? "*" : pattern;
        var files = Directory.EnumerateFiles(searchRoot, searchPattern, SearchOption.AllDirectories)
            .Take(200)
            .Select(f =>
            {
                var info     = new FileInfo(f);
                var relative = Path.GetRelativePath(_workspacePath, f);
                return new
                {
                    path        = relative,
                    size_bytes  = info.Length,
                    modified_at = info.LastWriteTimeUtc.ToString("O")
                };
            })
            .ToList();

        return Task.FromResult(JsonSerializer.Serialize(files, WriteOpts));
    }

    private Task<string> DeleteFileAsync(JsonDocument input)
    {
        if (!_available)
            return Task.FromResult(Error("Workspace is unavailable — directory missing or not writable"));

        var root     = input.RootElement;
        var filename = root.GetProperty("filename").GetString()!;

        var resolvedPath = ResolvePath(filename);
        if (resolvedPath is null)
            return Task.FromResult(Error("Path traversal detected — filename must resolve within the workspace"));

        if (File.Exists(resolvedPath))
        {
            File.Delete(resolvedPath);
            return Task.FromResult(JsonSerializer.Serialize(new { deleted = true }, WriteOpts));
        }

        if (Directory.Exists(resolvedPath))
        {
            Directory.Delete(resolvedPath);  // only succeeds for empty directories
            return Task.FromResult(JsonSerializer.Serialize(new { deleted = true }, WriteOpts));
        }

        return Task.FromResult(Error($"Not found: '{filename}'"));
    }

    private Task<string> GetInfoAsync()
    {
        if (!_available)
            return Task.FromResult(JsonSerializer.Serialize(
                new { workspace_path = _workspacePath, total_files = 0, total_size_bytes = 0L, available = false },
                WriteOpts));

        var files     = Directory.EnumerateFiles(_workspacePath, "*", SearchOption.AllDirectories).ToList();
        var totalSize = files.Select(f => new FileInfo(f).Length).Sum();

        return Task.FromResult(JsonSerializer.Serialize(
            new
            {
                workspace_path   = _workspacePath,
                total_files      = files.Count,
                total_size_bytes = totalSize,
                available        = true
            },
            WriteOpts));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a relative filename against the workspace root.
    /// Returns null if the resolved path escapes the workspace (path traversal guard).
    /// </summary>
    private string? ResolvePath(string filename)
    {
        filename = filename.TrimStart('/', '\\');

        var fullPath = Path.GetFullPath(Path.Combine(_workspacePath, filename));
        var root     = Path.GetFullPath(_workspacePath);

        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && fullPath != root)
        {
            _logger.LogWarning("[WorkspaceModule] Path traversal attempt: '{Filename}' resolved to '{FullPath}'", filename, fullPath);
            return null;
        }

        return fullPath;
    }

    private static string Error(string message) =>
        JsonSerializer.Serialize(new { error = message }, WriteOpts);
}
