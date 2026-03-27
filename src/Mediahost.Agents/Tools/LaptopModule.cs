using System.Text.Json;
using Mediahost.Llm.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Mediahost.Agents.Tools;

/// <summary>
/// Tool module that forwards calls to the user's registered laptop via Jarvis DeviceHub.
/// Requires at least one device to be online and the LAH to be running on the laptop.
///
/// Register in any agent's DI setup:
///   services.AddScoped&lt;IToolModule, LaptopModule&gt;();
///
/// Requires configuration: Jarvis:BaseUrl (default: http://jarvis-api:5000)
/// </summary>
public class LaptopModule(
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ILogger<LaptopModule> logger) : IToolModule
{
    private static readonly JsonSerializerOptions Opts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public IEnumerable<ToolDefinition> GetDefinitions() =>
    [
        // ── Filesystem ────────────────────────────────────────────────────────
        new ToolDefinition(
            "laptop_read_file",
            "Read the contents of a file on Gert's laptop. Use ~ for the home directory.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "path":         { "type": "string", "description": "Absolute or ~ path to the file" },
                "max_lines":    { "type": "number", "description": "Maximum lines to return (default: 200)" }
              },
              "required": ["path"]
            }
            """)),

        new ToolDefinition(
            "laptop_list_directory",
            "List files and folders in a directory on Gert's laptop.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "path":      { "type": "string",  "description": "Absolute or ~ path to the directory" },
                "recursive": { "type": "boolean", "description": "Include subdirectories (default: false)" }
              },
              "required": ["path"]
            }
            """)),

        new ToolDefinition(
            "laptop_file_exists",
            "Check whether a file or directory exists on Gert's laptop.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Absolute or ~ path to check" }
              },
              "required": ["path"]
            }
            """)),

        new ToolDefinition(
            "laptop_disk_usage",
            "Get disk usage statistics for a path on Gert's laptop.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Path to check disk usage for (default: ~)" }
              }
            }
            """)),

        new ToolDefinition(
            "laptop_write_file",
            "Write content to a file on Gert's laptop. Requires confirmation.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "path":    { "type": "string", "description": "Absolute or ~ path to write to" },
                "content": { "type": "string", "description": "Content to write" },
                "append":  { "type": "boolean", "description": "Append instead of overwrite (default: false)" }
              },
              "required": ["path", "content"]
            }
            """)),

        // ── System ────────────────────────────────────────────────────────────
        new ToolDefinition(
            "laptop_disk_report",
            "Get disk space for ALL mounted drives and partitions on Gert's laptop using df -h. " +
            "Use this to check available space on any drive, including secondary or external drives. " +
            "Shows filesystem, size, used, available, and mount point for every partition.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {}
            }
            """)),

        new ToolDefinition(
            "laptop_dir_sizes",
            "Show the top 20 largest files/directories under a given path on Gert's laptop (du -sh).",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Absolute or ~ path to scan (default: ~)" }
              }
            }
            """)),

        new ToolDefinition(
            "laptop_memory_usage",
            "Get current RAM and swap usage on Gert's laptop.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {}
            }
            """)),

        new ToolDefinition(
            "laptop_process_list",
            "List running processes on Gert's laptop, optionally filtered by name.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "filter": { "type": "string", "description": "Optional name filter (case-insensitive substring)" }
              }
            }
            """)),

        new ToolDefinition(
            "laptop_find_large_files",
            "Find the largest files in a directory on Gert's laptop.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "path":      { "type": "string", "description": "Root directory to search (default: ~)" },
                "top":       { "type": "number", "description": "Number of results to return (default: 20)" },
                "min_mb":    { "type": "number", "description": "Minimum size in MB to include (default: 100)" }
              }
            }
            """)),

        // ── Podman ────────────────────────────────────────────────────────────
        new ToolDefinition(
            "laptop_podman_ps",
            "List all Podman containers on Gert's laptop (running and stopped).",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "all": { "type": "boolean", "description": "Show all containers including stopped (default: true)" }
              }
            }
            """)),

        new ToolDefinition(
            "laptop_podman_logs",
            "Get logs from a Podman container on Gert's laptop.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "container": { "type": "string", "description": "Container name or ID" },
                "tail":      { "type": "number", "description": "Number of lines from end (default: 100)" }
              },
              "required": ["container"]
            }
            """)),

        new ToolDefinition(
            "laptop_podman_stats",
            "Get CPU and memory stats for running Podman containers on Gert's laptop.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {}
            }
            """)),

        new ToolDefinition(
            "laptop_podman_prune",
            "Remove stopped Podman containers and unused images on Gert's laptop to free up disk space. Requires confirmation.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "images": { "type": "boolean", "description": "Also prune unused images (default: false)" }
              }
            }
            """)),

        // ── Git ───────────────────────────────────────────────────────────────
        new ToolDefinition(
            "laptop_git_status",
            "Get the git status of a repository on Gert's laptop.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "repo_path": { "type": "string", "description": "Absolute or ~ path to the git repository" }
              },
              "required": ["repo_path"]
            }
            """)),

        new ToolDefinition(
            "laptop_git_log",
            "Get the recent git commit log for a repository on Gert's laptop.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "repo_path": { "type": "string", "description": "Absolute or ~ path to the git repository" },
                "limit":     { "type": "number", "description": "Number of commits to show (default: 10)" }
              },
              "required": ["repo_path"]
            }
            """)),

        new ToolDefinition(
            "laptop_git_pull",
            "Pull latest changes for a repository on Gert's laptop.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "repo_path": { "type": "string", "description": "Absolute or ~ path to the git repository" }
              },
              "required": ["repo_path"]
            }
            """)),

        new ToolDefinition(
            "laptop_git_status_all",
            "Scan all git repositories under a root path on Gert's laptop and return their status (clean/dirty/ahead/behind). Useful to check the state of all projects at once.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "root_path": { "type": "string", "description": "Root directory to scan for git repos (default: ~/Documents)" }
              }
            }
            """)),

        // ── App Launcher ──────────────────────────────────────────────────────
        new ToolDefinition(
            "laptop_open_url",
            "Open a URL in the default browser on Gert's laptop.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "url": { "type": "string", "description": "The URL to open" }
              },
              "required": ["url"]
            }
            """)),

        new ToolDefinition(
            "laptop_open_file",
            "Open a file with its default application on Gert's laptop.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Absolute or ~ path to the file to open" }
              },
              "required": ["path"]
            }
            """)),
    ];

    public async Task<string?> TryExecuteAsync(string toolName, JsonDocument input, CancellationToken ct = default)
    {
        if (!toolName.StartsWith("laptop_")) return null;

        try
        {
            var baseUrl = config["Jarvis:BaseUrl"] ?? "http://jarvis-api:5000";
            var client  = httpFactory.CreateClient();
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout     = TimeSpan.FromSeconds(45);

            var body    = new { toolName, parameters = input.RootElement, requireConfirm = false };
            var json    = JsonSerializer.Serialize(body, Opts);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            logger.LogInformation("[LaptopModule] Forwarding tool '{Tool}' via Jarvis", toolName);
            var response = await client.PostAsync("/api/devices/tools/execute", content, ct);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("[LaptopModule] Jarvis returned {Status}: {Err}", response.StatusCode, err);
                return JsonSerializer.Serialize(new { error = $"Jarvis returned {(int)response.StatusCode}", detail = err });
            }

            var resp = await response.Content.ReadAsStringAsync(ct);
            // Unwrap { result: "..." } envelope
            using var doc = JsonDocument.Parse(resp);
            if (doc.RootElement.TryGetProperty("result", out var resultEl))
                return resultEl.GetString() ?? resp;

            return resp;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LaptopModule] Tool '{Tool}' failed", toolName);
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }
}
