using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LaptopHost.Modules;

public class GitModule(ILogger<GitModule> logger) : ILaptopToolModule
{
    private static readonly JsonSerializerOptions Opts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public string ModuleName => "git";

    public IEnumerable<LaptopToolSpec> GetDefinitions() =>
    [
        new("laptop_git_status",
            "Show uncommitted changes and unpushed commits in a git repository",
            """{"type":"object","properties":{"repo_path":{"type":"string"}},"required":["repo_path"]}"""),

        new("laptop_git_status_all",
            "Scan all git repositories under a root directory and return their status as a summary table",
            """{"type":"object","properties":{"projects_root":{"type":"string"}},"required":["projects_root"]}"""),

        new("laptop_git_pull",
            "Pull the latest changes from the remote in a git repository",
            """{"type":"object","properties":{"repo_path":{"type":"string"}},"required":["repo_path"]}"""),

        new("laptop_git_commit",
            "Stage all changes and create a commit in a git repository",
            """{"type":"object","properties":{"repo_path":{"type":"string"},"message":{"type":"string"}},"required":["repo_path","message"]}""",
            RequireConfirm: true),

        new("laptop_git_push",
            "Push local commits to the remote in a git repository",
            """{"type":"object","properties":{"repo_path":{"type":"string"}},"required":["repo_path"]}""",
            RequireConfirm: true),

        new("laptop_git_log",
            "Show recent commit history for a git repository",
            """{"type":"object","properties":{"repo_path":{"type":"string"},"count":{"type":"integer","default":10}},"required":["repo_path"]}""")
    ];

    public async Task<string> ExecuteAsync(string toolName, JsonDocument parameters, CancellationToken ct = default)
    {
        try
        {
            var root = parameters.RootElement;
            return toolName switch
            {
                "laptop_git_status"     => await GitStatusAsync(root, ct),
                "laptop_git_status_all" => await GitStatusAllAsync(root, ct),
                "laptop_git_pull"       => await RunGitAsync(root, "pull", ct),
                "laptop_git_commit"     => await GitCommitAsync(root, ct),
                "laptop_git_push"       => await RunGitAsync(root, "push", ct),
                "laptop_git_log"        => await GitLogAsync(root, ct),
                _ => Err($"Unknown tool: {toolName}")
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Git] Tool '{Tool}' failed", toolName);
            return Err(ex.Message);
        }
    }

    private async Task<string> GitStatusAsync(JsonElement p, CancellationToken ct)
    {
        var repo   = Expand(p.GetProperty("repo_path").GetString()!);
        var status = await ExecGitAsync(repo, "status --porcelain", ct);
        var branch = await ExecGitAsync(repo, "branch --show-current", ct);
        var ahead  = await ExecGitAsync(repo, "rev-list --count @{u}..HEAD 2>/dev/null || echo 0", ct);

        return Ok(new
        {
            repo,
            branch       = branch.Trim(),
            uncommitted  = status.Trim(),
            unpushed_commits = int.TryParse(ahead.Trim(), out var n) ? n : 0,
            is_clean     = string.IsNullOrWhiteSpace(status.Trim())
        });
    }

    private async Task<string> GitStatusAllAsync(JsonElement p, CancellationToken ct)
    {
        var root = Expand(p.GetProperty("projects_root").GetString()!);
        if (!Directory.Exists(root))
            return Err($"Directory not found: {root}");

        var repos = Directory.GetDirectories(root, ".git", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .Where(d => d is not null)
            .Cast<string>()
            .ToList();

        var results = new List<object>();
        foreach (var repo in repos)
        {
            var status = await ExecGitAsync(repo, "status --porcelain", ct);
            var branch = await ExecGitAsync(repo, "branch --show-current", ct);
            var lastLog = await ExecGitAsync(repo, "log -1 --format='%h %s'", ct);

            results.Add(new
            {
                repo         = Path.GetFileName(repo),
                path         = repo,
                branch       = branch.Trim(),
                uncommitted  = string.IsNullOrWhiteSpace(status.Trim()) ? "clean" : status.Trim().Split('\n').Length + " changes",
                last_commit  = lastLog.Trim()
            });
        }

        return Ok(new { projects_root = root, count = results.Count, repos = results });
    }

    private async Task<string> GitCommitAsync(JsonElement p, CancellationToken ct)
    {
        var repo    = Expand(p.GetProperty("repo_path").GetString()!);
        var message = p.GetProperty("message").GetString()!;

        await ExecGitAsync(repo, "add -A", ct);
        var result = await ExecGitAsync(repo, $"commit -m \"{message.Replace("\"", "\\\"")}\"", ct);

        return Ok(new { repo, message, output = result.Trim() });
    }

    private async Task<string> GitLogAsync(JsonElement p, CancellationToken ct)
    {
        var repo  = Expand(p.GetProperty("repo_path").GetString()!);
        var count = p.TryGetProperty("count", out var c) ? c.GetInt32() : 10;

        var log = await ExecGitAsync(repo, $"log -{count} --oneline --format='%h|%an|%ar|%s'", ct);
        var commits = log.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                var parts = line.Split('|', 4);
                return new
                {
                    hash    = parts.ElementAtOrDefault(0)?.Trim(),
                    author  = parts.ElementAtOrDefault(1)?.Trim(),
                    when    = parts.ElementAtOrDefault(2)?.Trim(),
                    message = parts.ElementAtOrDefault(3)?.Trim()
                };
            }).ToList();

        return Ok(new { repo, commits });
    }

    private async Task<string> RunGitAsync(JsonElement p, string command, CancellationToken ct)
    {
        var repo   = Expand(p.GetProperty("repo_path").GetString()!);
        var output = await ExecGitAsync(repo, command, ct);
        return Ok(new { repo, command, output = output.Trim() });
    }

    private async Task<string> ExecGitAsync(string workingDir, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory       = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git");

        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        return stdout;
    }

    private static string Expand(string path) =>
        path.StartsWith("~/") ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            path[2..]) : path;

    private static string Ok(object value)  => JsonSerializer.Serialize(value, Opts);
    private static string Err(string msg)   => JsonSerializer.Serialize(new { error = msg }, Opts);
}
