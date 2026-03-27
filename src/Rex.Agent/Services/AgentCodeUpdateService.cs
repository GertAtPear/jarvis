using System.Text.Json;
using Dapper;
using Mediahost.Agents.Data;
using Rex.Agent.Data.Repositories;

namespace Rex.Agent.Services;

public record CodeUpdateResult(
    bool    Success,
    string  AgentName,
    string? CommitSha,
    string? ErrorMessage = null);

public class AgentCodeUpdateService(
    DeveloperAgentService  devAgent,
    ContainerService       containers,
    DbConnectionFactory    db,
    AgentUpdateRepository  updateRepo,
    IConfiguration         config,
    ILogger<AgentCodeUpdateService> logger)
{
    private string ProjectRoot => config["Rex:ProjectRoot"] ?? "/project";

    public async Task<string> PlanCodeUpdateAsync(
        string agentName, string changeDescription, CancellationToken ct = default)
    {
        var agentDir = Path.Combine(ProjectRoot, "src", $"{agentName}.Agent");
        if (!Directory.Exists(agentDir))
            throw new DirectoryNotFoundException($"Agent source not found: {agentDir}");

        // Read key source files for context
        var contextFiles = new Dictionary<string, string>();
        foreach (var f in new[] { $"Tools/{agentName}ToolExecutor.cs", $"Tools/{agentName}ToolDefinitions.cs",
                                   $"Services/{agentName}AgentService.cs" })
        {
            var path = Path.Combine(agentDir, f);
            if (File.Exists(path))
                contextFiles[f] = await File.ReadAllTextAsync(path, ct);
        }

        return await devAgent.PlanTaskAsync(
            $"Plan changes to the {agentName} agent:\n\n{changeDescription}",
            contextFiles, ct);
    }

    public async Task<CodeUpdateResult> ExecuteCodeUpdateAsync(
        string   agentName,
        string   planSummary,
        string[] filesToModify,
        CancellationToken ct = default)
    {
        var agentDir   = Path.Combine(ProjectRoot, "src", $"{agentName}.Agent");
        var agentNameL = agentName.ToLower();

        await using var conn = db.Create();
        var wasScaffolded = await conn.ExecuteScalarAsync<bool>(
            "SELECT was_scaffolded FROM jarvis_schema.agents WHERE LOWER(name) = LOWER(@agentName)",
            new { agentName });

        var modifiedFiles = new List<string>();

        try
        {
            // Read current file content for context
            var allContext = new Dictionary<string, string>();
            foreach (var rel in filesToModify)
            {
                var path = Path.Combine(agentDir, rel);
                if (File.Exists(path))
                    allContext[rel] = await File.ReadAllTextAsync(path, ct);
            }

            // Develop each file via sub-agent
            foreach (var rel in filesToModify)
            {
                var path    = Path.Combine(agentDir, rel);
                var content = await devAgent.DevelopFileAsync(
                    $"Implement the following change to {agentName}:\n{planSummary}\n\nFile to modify: {rel}",
                    rel, allContext, ct);

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, content, ct);
                modifiedFiles.Add(rel);
                logger.LogInformation("Updated {File} for {Agent}", rel, agentName);
            }

            // Dotnet build check
            var (buildOk, buildOutput) = await RunProcessAsync(
                "dotnet", $"build {agentDir}/{agentName}.Agent.csproj --no-restore -v q", ct);

            if (!buildOk)
            {
                logger.LogError("Build failed for {Agent}: {Output}", agentName, buildOutput[^Math.Min(1000, buildOutput.Length)..]);
                // Revert changes
                await RunProcessAsync("git", $"-C {ProjectRoot} checkout -- .", ct);
                await updateRepo.LogAsync(new AgentUpdateRecord
                {
                    AgentName     = agentName,
                    Operation     = "code_update",
                    WasScaffolded = wasScaffolded,
                    Description   = planSummary,
                    FilesModified = System.Text.Json.JsonSerializer.Serialize(filesToModify),
                    PerformedBy   = "rex",
                    Success       = false,
                    ErrorDetails  = buildOutput[^Math.Min(500, buildOutput.Length)..]
                });
                return new CodeUpdateResult(false, agentName, null,
                    $"Build failed (changes reverted):\n{buildOutput[^Math.Min(500, buildOutput.Length)..]}");
            }

            // Container build + restart
            await containers.BuildImageAsync(
                Path.Combine("src", $"{agentName}.Agent", "Dockerfile"),
                ProjectRoot,
                $"{agentNameL}-agent:latest",
                ct);

            await RunProcessAsync("podman",
                $"compose -f {Path.Combine(ProjectRoot, "docker-compose.yml")} restart {agentNameL}-agent", ct);

            // Git commit
            await RunProcessAsync("git", $"-C {ProjectRoot} add -A", ct);
            await RunProcessAsync("git",
                $"-C {ProjectRoot} commit -m \"feat({agentNameL}): {planSummary}\"", ct);
            await RunProcessAsync("git", $"-C {ProjectRoot} push", ct);

            var (_, shaOut) = await RunProcessAsync("git",
                $"-C {ProjectRoot} rev-parse --short HEAD", ct);
            var sha = shaOut.Trim();

            await updateRepo.LogAsync(new AgentUpdateRecord
            {
                AgentName     = agentName,
                Operation     = "code_update",
                WasScaffolded = wasScaffolded,
                Description   = planSummary,
                FilesModified = System.Text.Json.JsonSerializer.Serialize(filesToModify),
                PerformedBy   = "rex",
                Success       = true,
            });

            return new CodeUpdateResult(true, agentName, sha);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Code update failed for {Agent}", agentName);
            await updateRepo.LogAsync(new AgentUpdateRecord
            {
                AgentName     = agentName,
                Operation     = "code_update",
                WasScaffolded = wasScaffolded,
                Description   = planSummary,
                PerformedBy   = "rex",
                Success       = false,
                ErrorDetails  = ex.Message,
            });
            return new CodeUpdateResult(false, agentName, null, ex.Message);
        }
    }

    private static async Task<(bool Success, string output)> RunProcessAsync(
        string exe, string args, CancellationToken ct)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return (proc.ExitCode == 0, stdout + stderr);
    }
}
