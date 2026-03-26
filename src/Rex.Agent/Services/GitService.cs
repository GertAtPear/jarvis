using System.Diagnostics;
using Rex.Agent.Models;

namespace Rex.Agent.Services;

public class GitService(IConfiguration config, ILogger<GitService> logger)
{
    private string WorkspacePath => config["Rex:WorkspacePath"] ?? "/workspace";

    public Task<ShellResult> CloneAsync(string url, string localName, CancellationToken ct = default)
    {
        var dest = Path.Combine(WorkspacePath, localName);
        return RunGitAsync(WorkspacePath, $"clone {url} {localName}", ct);
    }

    public Task<ShellResult> PullAsync(string repoPath, CancellationToken ct = default) =>
        RunGitAsync(repoPath, "pull", ct);

    public Task<ShellResult> StatusAsync(string repoPath, CancellationToken ct = default) =>
        RunGitAsync(repoPath, "status --short", ct);

    public Task<ShellResult> DiffAsync(string repoPath, CancellationToken ct = default) =>
        RunGitAsync(repoPath, "diff HEAD", ct);

    public Task<ShellResult> LogAsync(string repoPath, int limit = 10, CancellationToken ct = default) =>
        RunGitAsync(repoPath, $"log --oneline -{limit}", ct);

    public Task<ShellResult> AddCommitAsync(string repoPath, string message, string[]? files = null, CancellationToken ct = default)
    {
        var fileArg = files is { Length: > 0 } ? string.Join(" ", files) : "-A";
        return RunBashAsync(repoPath, $"git add {fileArg} && git commit -m \"{EscapeShell(message)}\"", ct);
    }

    public Task<ShellResult> PushAsync(string repoPath, string? branch = null, CancellationToken ct = default)
    {
        var branchArg = branch is not null ? $" origin {branch}" : "";
        return RunGitAsync(repoPath, $"push{branchArg}", ct);
    }

    public Task<ShellResult> CreateBranchAsync(string repoPath, string branchName, CancellationToken ct = default) =>
        RunGitAsync(repoPath, $"checkout -b {branchName}", ct);

    public Task<ShellResult> CheckoutAsync(string repoPath, string branchOrCommit, CancellationToken ct = default) =>
        RunGitAsync(repoPath, $"checkout {branchOrCommit}", ct);

    // ── Internal helpers ───────────────────────────────────────────────────────

    private async Task<ShellResult> RunGitAsync(string workDir, string args, CancellationToken ct)
    {
        logger.LogDebug("git {Args} in {Dir}", args, workDir);

        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory       = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };
        ConfigureGitEnv(psi);

        using var proc = Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        return new ShellResult(proc.ExitCode, stdout.Trim(), stderr.Trim());
    }

    private async Task<ShellResult> RunBashAsync(string workDir, string command, CancellationToken ct)
    {
        logger.LogDebug("bash -c '{Command}' in {Dir}", command, workDir);

        var psi = new ProcessStartInfo("bash", ["-c", command])
        {
            WorkingDirectory       = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };
        ConfigureGitEnv(psi);

        using var proc = Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        return new ShellResult(proc.ExitCode, stdout.Trim(), stderr.Trim());
    }

    private void ConfigureGitEnv(ProcessStartInfo psi)
    {
        var name  = config["Rex:GitUserName"]  ?? "Rex Agent";
        var email = config["Rex:GitUserEmail"] ?? "rex@mediahost.ai";
        psi.Environment["GIT_AUTHOR_NAME"]     = name;
        psi.Environment["GIT_AUTHOR_EMAIL"]    = email;
        psi.Environment["GIT_COMMITTER_NAME"]  = name;
        psi.Environment["GIT_COMMITTER_EMAIL"] = email;
    }

    private static string EscapeShell(string s) => s.Replace("\"", "\\\"");
}
