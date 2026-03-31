using System.Text.Json;
using Mediahost.Agents.Capabilities;
using Mediahost.Agents.Services;
using Mediahost.Llm.Models;
using Mediahost.Shared.Services;
using Mediahost.Tools.Models;
using Rex.Agent.Data;
using Rex.Agent.Data.Repositories;
using Rex.Agent.Services;
using StackExchange.Redis;

namespace Rex.Agent.Tools;

public class RexToolExecutor(
    GitService git,
    GitHubService gitHub,
    ContainerService containers,
    DeveloperAgentService devAgent,
    TesterAgentService testerAgent,
    SshCapability ssh,
    HttpCapability http,
    RexMemoryService memory,
    DeploymentRecipeRepository deploymentRecipes,
    ScaffoldingPlanService scaffoldingPlan,
    AgentScaffoldingService scaffolding,
    AgentMetadataService agentMetadata,
    AgentCodeUpdateService codeUpdate,
    ScaffoldingSessionRepository scaffoldingRepo,
    ScaffoldedAgentRepository scaffoldedRepo,
    PortRegistryRepository portRegistry,
    AgentUpdateRepository updateRepo,
    IConnectionMultiplexer redis,
    IConfiguration config,
    IScopedVaultService vault,
    ILogger<RexToolExecutor> logger) : IAgentToolExecutor
{
    public IReadOnlyList<ToolDefinition> GetTools() => RexToolDefinitions.GetTools();

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task<string> ExecuteAsync(string toolName, JsonDocument input, CancellationToken ct)
    {
        try
        {
            return toolName switch
            {
                // Filesystem
                "read_file"       => await ReadFileAsync(input, ct),
                "write_file"      => await WriteFileAsync(input, ct),
                "list_directory"  => await ListDirectoryAsync(input, ct),
                "delete_file"     => await DeleteFileAsync(input, ct),
                "search_code"     => await SearchCodeAsync(input, ct),

                // Git
                "git_status"        => await GitStatusAsync(input, ct),
                "git_clone"         => await GitCloneAsync(input, ct),
                "git_pull"          => await GitPullAsync(input, ct),
                "git_diff"          => await GitDiffAsync(input, ct),
                "git_log"           => await GitLogAsync(input, ct),
                "git_add_commit"    => await GitAddCommitAsync(input, ct),
                "git_push"          => await GitPushAsync(input, ct),
                "git_create_branch" => await GitCreateBranchAsync(input, ct),

                // GitHub API
                "gh_list_repos"   => await GhListReposAsync(ct),
                "gh_get_repo"     => await GhGetRepoAsync(input, ct),
                "gh_create_repo"  => await GhCreateRepoAsync(input, ct),
                "gh_list_branches"=> await GhListBranchesAsync(input, ct),
                "gh_create_pr"    => await GhCreatePrAsync(input, ct),
                "gh_list_issues"  => await GhListIssuesAsync(input, ct),
                "gh_update_file"  => await GhUpdateFileAsync(input, ct),

                // Developer agent
                "plan_task"       => await PlanTaskAsync(input, ct),
                "develop_file"    => await DevelopFileAsync(input, ct),
                "review_changes"  => await ReviewChangesAsync(input, ct),

                // Containers
                "container_list"    => await ContainerListAsync(ct),
                "container_logs"    => await ContainerLogsAsync(input, ct),
                "container_build"   => await ContainerBuildAsync(input, ct),
                "container_push"    => await ContainerPushAsync(input, ct),
                "container_restart" => await ContainerRestartAsync(input, ct),
                "container_inspect" => await ContainerInspectAsync(input, ct),

                // CI/CD
                "create_workflow" => await CreateWorkflowAsync(input, ct),
                "list_workflows"  => await ListWorkflowsAsync(input, ct),
                "read_workflow"   => await ReadWorkflowAsync(input, ct),

                // Memory
                "remember_fact" => await RememberFactAsync(input, ct),
                "forget_fact"   => await ForgetFactAsync(input, ct),

                // Agent Scaffolding
                "intake_agent"             => await IntakeAgentAsync(input, ct),
                "save_intake_answers"      => await SaveIntakeAnswersAsync(input, ct),
                "present_scaffolding_plan" => await PresentScaffoldingPlanAsync(input, ct),
                "approve_scaffolding"      => await ApproveScaffoldingAsync(input, ct),
                "list_scaffolded_agents"   => await ListScaffoldedAgentsAsync(input, ct),
                "list_port_registry"       => await ListPortRegistryAsync(ct),

                // Agent Lifecycle
                "get_agent_info"           => await GetAgentInfoAsync(input, ct),
                "update_agent_metadata"    => await UpdateAgentMetadataAsync(input, ct),
                "plan_agent_code_update"   => await PlanAgentCodeUpdateAsync(input, ct),
                "execute_agent_code_update"=> await ExecuteAgentCodeUpdateAsync(input, ct),
                "soft_retire_agent"        => await SoftRetireAgentAsync(input, ct),
                "hard_retire_agent"        => await HardRetireAgentAsync(input, ct),
                "reactivate_agent"         => await ReactivateAgentAsync(input, ct),
                "list_agents"              => await ListAgentsAsync(input, ct),

                // Tester Agent
                "save_test_spec"   => await SaveTestSpecAsync(input, ct),
                "get_test_spec"    => await GetTestSpecAsync(input, ct),
                "list_test_specs"  => await ListTestSpecsAsync(ct),
                "run_test_suite"   => await RunTestSuiteAsync(input, ct),

                // Deployment Recipes
                "save_deployment_recipe"  => await SaveDeploymentRecipeAsync(input, ct),
                "get_deployment_recipe"   => await GetDeploymentRecipeAsync(input, ct),
                "list_deployment_recipes" => await ListDeploymentRecipesAsync(ct),
                "execute_deployment"      => await ExecuteDeploymentAsync(input, ct),

                _ => Err($"Unknown tool: {toolName}")
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tool execution failed: {Tool}", toolName);
            return Err(ex.Message);
        }
    }

    // ── Filesystem ─────────────────────────────────────────────────────────────

    private Task<string> ReadFileAsync(JsonDocument input, CancellationToken ct)
    {
        var path = RequireString(input, "path");
        if (!File.Exists(path))
            return Task.FromResult(Err($"File not found: {path}"));

        var content = File.ReadAllText(path);
        return Task.FromResult(Ok(new { path, content, size = content.Length }));
    }

    private Task<string> WriteFileAsync(JsonDocument input, CancellationToken ct)
    {
        var path    = RequireString(input, "path");
        var content = RequireString(input, "content");

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, content);
        return Task.FromResult(Ok(new { path, bytes = content.Length, message = "File written." }));
    }

    private Task<string> ListDirectoryAsync(JsonDocument input, CancellationToken ct)
    {
        var path      = RequireString(input, "path");
        var recursive = GetBool(input, "recursive") ?? false;

        if (!Directory.Exists(path))
            return Task.FromResult(Err($"Directory not found: {path}"));

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var entries = Directory.EnumerateFileSystemEntries(path, "*", option)
            .Select(e => new { path = e, type = Directory.Exists(e) ? "dir" : "file" })
            .Take(500)   // sanity cap
            .ToList();

        return Task.FromResult(Ok(new { path, count = entries.Count, entries }));
    }

    private Task<string> DeleteFileAsync(JsonDocument input, CancellationToken ct)
    {
        var path = RequireString(input, "path");
        if (!File.Exists(path))
            return Task.FromResult(Err($"File not found: {path}"));

        File.Delete(path);
        return Task.FromResult(Ok(new { path, message = "File deleted." }));
    }

    private async Task<string> SearchCodeAsync(JsonDocument input, CancellationToken ct)
    {
        var directory = RequireString(input, "directory");
        var pattern   = RequireString(input, "pattern");
        var glob      = GetString(input, "glob") ?? "*";

        var psi = new System.Diagnostics.ProcessStartInfo(
            "grep", $"-r --include=\"{glob}\" -n \"{pattern}\" .")
        {
            WorkingDirectory       = directory,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };

        using var proc = System.Diagnostics.Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(200).ToList();
        return Ok(new { directory, pattern, match_count = lines.Count, matches = lines });
    }

    // ── Git ────────────────────────────────────────────────────────────────────

    private async Task<string> GitStatusAsync(JsonDocument input, CancellationToken ct)
    {
        var r = await git.StatusAsync(RequireString(input, "repo_path"), ct);
        return ShellOk(r);
    }

    private async Task<string> GitCloneAsync(JsonDocument input, CancellationToken ct)
    {
        var url  = RequireString(input, "url");
        var name = RequireString(input, "local_name");
        var r    = await git.CloneAsync(url, name, ct);
        return ShellOk(r);
    }

    private async Task<string> GitPullAsync(JsonDocument input, CancellationToken ct)
    {
        var r = await git.PullAsync(RequireString(input, "repo_path"), ct);
        return ShellOk(r);
    }

    private async Task<string> GitDiffAsync(JsonDocument input, CancellationToken ct)
    {
        var r = await git.DiffAsync(RequireString(input, "repo_path"), ct);
        return ShellOk(r);
    }

    private async Task<string> GitLogAsync(JsonDocument input, CancellationToken ct)
    {
        var limit = GetInt(input, "limit") ?? 10;
        var r     = await git.LogAsync(RequireString(input, "repo_path"), limit, ct);
        return ShellOk(r);
    }

    private async Task<string> GitAddCommitAsync(JsonDocument input, CancellationToken ct)
    {
        var repoPath = RequireString(input, "repo_path");
        var message  = RequireString(input, "message");
        string[]? files = null;

        if (input.RootElement.TryGetProperty("files", out var filesEl))
            files = filesEl.EnumerateArray().Select(e => e.GetString()!).ToArray();

        var r = await git.AddCommitAsync(repoPath, message, files, ct);
        return ShellOk(r);
    }

    private async Task<string> GitPushAsync(JsonDocument input, CancellationToken ct)
    {
        var branch = GetString(input, "branch");
        var r      = await git.PushAsync(RequireString(input, "repo_path"), branch, ct);
        return ShellOk(r);
    }

    private async Task<string> GitCreateBranchAsync(JsonDocument input, CancellationToken ct)
    {
        var r = await git.CreateBranchAsync(
            RequireString(input, "repo_path"),
            RequireString(input, "branch_name"), ct);
        return ShellOk(r);
    }

    // ── GitHub API ─────────────────────────────────────────────────────────────

    private async Task<string> GhListReposAsync(CancellationToken ct)
    {
        var repos = await gitHub.ListReposAsync(ct);
        var items = repos.Select(r => new { r.FullName, r.Description, r.Language, r.StargazersCount, r.Private }).ToList();
        return Ok(new { count = items.Count, repos = items });
    }

    private async Task<string> GhGetRepoAsync(JsonDocument input, CancellationToken ct)
    {
        var repo = await gitHub.GetRepoAsync(
            RequireString(input, "owner"), RequireString(input, "repo"), ct);
        return Ok(new { repo.FullName, repo.Description, repo.Language, repo.StargazersCount, repo.Private, repo.DefaultBranch, repo.CloneUrl });
    }

    private async Task<string> GhCreateRepoAsync(JsonDocument input, CancellationToken ct)
    {
        var repo = await gitHub.CreateRepoAsync(
            RequireString(input, "name"),
            GetString(input, "description") ?? "",
            GetBool(input, "private") ?? false, ct);
        return Ok(new { repo.FullName, repo.CloneUrl, message = "Repository created." });
    }

    private async Task<string> GhListBranchesAsync(JsonDocument input, CancellationToken ct)
    {
        var branches = await gitHub.ListBranchesAsync(
            RequireString(input, "owner"), RequireString(input, "repo"), ct);
        return Ok(new { count = branches.Count, branches = branches.Select(b => b.Name).ToList() });
    }

    private async Task<string> GhCreatePrAsync(JsonDocument input, CancellationToken ct)
    {
        var pr = await gitHub.CreatePrAsync(
            RequireString(input, "owner"),
            RequireString(input, "repo"),
            RequireString(input, "title"),
            GetString(input, "body") ?? "",
            RequireString(input, "head"),
            GetString(input, "base") ?? "main", ct);
        return Ok(new { pr.Number, pr.Title, pr.HtmlUrl, message = "Pull request created." });
    }

    private async Task<string> GhListIssuesAsync(JsonDocument input, CancellationToken ct)
    {
        var issues = await gitHub.ListIssuesAsync(
            RequireString(input, "owner"), RequireString(input, "repo"), ct);
        var items = issues.Select(i => new { i.Number, i.Title, i.State, i.HtmlUrl }).ToList();
        return Ok(new { count = items.Count, issues = items });
    }

    private async Task<string> GhUpdateFileAsync(JsonDocument input, CancellationToken ct)
    {
        await gitHub.CreateOrUpdateFileAsync(
            RequireString(input, "owner"),
            RequireString(input, "repo"),
            RequireString(input, "path"),
            RequireString(input, "content"),
            RequireString(input, "message"),
            GetString(input, "branch"), ct);
        return Ok(new { message = "File updated on GitHub." });
    }

    // ── Developer Agent ────────────────────────────────────────────────────────

    private async Task<string> PlanTaskAsync(JsonDocument input, CancellationToken ct)
    {
        var task    = RequireString(input, "task");
        var context = GetContextFiles(input);
        var plan    = await devAgent.PlanTaskAsync(task, context, ct);
        return Ok(new { plan });
    }

    private async Task<string> DevelopFileAsync(JsonDocument input, CancellationToken ct)
    {
        var task       = RequireString(input, "task");
        var targetFile = RequireString(input, "target_file");
        var context    = GetContextFiles(input);
        var content    = await devAgent.DevelopFileAsync(task, targetFile, context, ct);
        return Ok(new { target_file = targetFile, content });
    }

    private async Task<string> ReviewChangesAsync(JsonDocument input, CancellationToken ct)
    {
        var diff = RequireString(input, "diff");
        var desc = RequireString(input, "task_description");
        var review = await devAgent.ReviewChangesAsync(diff, desc, ct);
        return Ok(new { review });
    }

    // ── Containers ─────────────────────────────────────────────────────────────

    private async Task<string> ContainerListAsync(CancellationToken ct)
    {
        var list = await containers.ListContainersAsync(ct);
        var items = list.Select(c => new { id = c.Id[..Math.Min(12, c.Id.Length)], name = c.Names.FirstOrDefault() ?? "", c.Image, c.State, c.Status }).ToList();
        return Ok(new { count = items.Count, containers = items });
    }

    private async Task<string> ContainerLogsAsync(JsonDocument input, CancellationToken ct)
    {
        var name = RequireString(input, "container_name");
        var tail = GetInt(input, "tail") ?? 100;
        var logs = await containers.GetLogsAsync(name, tail, ct);
        return Ok(new { container = name, logs });
    }

    private async Task<string> ContainerBuildAsync(JsonDocument input, CancellationToken ct)
    {
        var dockerfile  = RequireString(input, "dockerfile");
        var contextPath = RequireString(input, "context_path");
        var tag         = RequireString(input, "tag");
        var (ok, output) = await containers.BuildImageAsync(dockerfile, contextPath, tag, ct);
        return Ok(new { success = ok, tag, output });
    }

    private async Task<string> ContainerPushAsync(JsonDocument input, CancellationToken ct)
    {
        var tag       = RequireString(input, "image_tag");
        var vaultPath = GetString(input, "vault_path") ?? "/rex/dockerhub";
        var registry  = GetString(input, "registry")   ?? "docker.io";

        var username = await vault.GetSecretAsync(vaultPath, "username", ct);
        var password = await vault.GetSecretAsync(vaultPath, "password", ct);

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return Err($"Docker Hub credentials not found at vault path '{vaultPath}'. " +
                       "Add 'username' and 'password' fields in Infisical.");

        var (loginOk, loginOutput) = await containers.LoginRegistryAsync(registry, username, password, ct);
        if (!loginOk)
            return Err($"Registry login failed: {loginOutput}");

        var (pushOk, pushOutput) = await containers.PushImageAsync(tag, ct);
        return Ok(new
        {
            success  = pushOk,
            tag,
            registry,
            output   = pushOutput,
            message  = pushOk
                ? $"Image '{tag}' pushed to {registry} successfully."
                : $"Push failed: {pushOutput}"
        });
    }

    private async Task<string> ContainerRestartAsync(JsonDocument input, CancellationToken ct)
    {
        var name = RequireString(input, "container_name");
        await containers.RestartContainerAsync(name, ct);
        return Ok(new { container = name, message = "Container restarted." });
    }

    private async Task<string> ContainerInspectAsync(JsonDocument input, CancellationToken ct)
    {
        var name = RequireString(input, "container_name");
        var json = await containers.InspectContainerAsync(name, ct);
        return Ok(new { container = name, inspect = json });
    }

    // ── CI/CD ──────────────────────────────────────────────────────────────────

    private Task<string> CreateWorkflowAsync(JsonDocument input, CancellationToken ct)
    {
        var repoPath     = RequireString(input, "repo_path");
        var workflowName = RequireString(input, "workflow_name");
        var content      = RequireString(input, "content");

        var dir  = Path.Combine(repoPath, ".github", "workflows");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, workflowName);
        File.WriteAllText(path, content);

        return Task.FromResult(Ok(new { path, message = "Workflow written." }));
    }

    private Task<string> ListWorkflowsAsync(JsonDocument input, CancellationToken ct)
    {
        var repoPath = RequireString(input, "repo_path");
        var dir      = Path.Combine(repoPath, ".github", "workflows");

        if (!Directory.Exists(dir))
            return Task.FromResult(Ok(new { workflows = Array.Empty<string>() }));

        var files = Directory.EnumerateFiles(dir, "*.yml")
            .Concat(Directory.EnumerateFiles(dir, "*.yaml"))
            .Select(Path.GetFileName)
            .ToList();

        return Task.FromResult(Ok(new { count = files.Count, workflows = files }));
    }

    private Task<string> ReadWorkflowAsync(JsonDocument input, CancellationToken ct)
    {
        var repoPath     = RequireString(input, "repo_path");
        var workflowName = RequireString(input, "workflow_name");
        var path         = Path.Combine(repoPath, ".github", "workflows", workflowName);

        if (!File.Exists(path))
            return Task.FromResult(Err($"Workflow not found: {workflowName}"));

        var content = File.ReadAllText(path);
        return Task.FromResult(Ok(new { path, content }));
    }

    // ── Memory ─────────────────────────────────────────────────────────────────

    private async Task<string> RememberFactAsync(JsonDocument input, CancellationToken ct)
    {
        var key   = RequireString(input, "key");
        var value = RequireString(input, "value");
        await memory.RememberFactAsync(key, value, ct);
        return Ok(new { remembered = true, key, message = $"Remembered: {key} = {value}" });
    }

    private async Task<string> ForgetFactAsync(JsonDocument input, CancellationToken ct)
    {
        var key = RequireString(input, "key");
        await memory.ForgetFactAsync(key, ct);
        return Ok(new { forgotten = true, key });
    }

    // ── Agent Scaffolding ───────────────────────────────────────────────────────

    private async Task<string> IntakeAgentAsync(JsonDocument input, CancellationToken ct)
    {
        var description = RequireString(input, "description");
        var sessionId   = await scaffoldingRepo.CreateSessionAsync(description);

        var intakeForm = config["Rex:ScaffoldingRoot"] is { } root
            ? await File.ReadAllTextAsync(Path.Combine(root, "templates/intake/questions.md"), ct)
            : ScaffoldingIntakeQuestions.Default;

        return Ok(new
        {
            session_id = sessionId,
            message    = $"Scaffolding session created. Please answer the following intake questions and call save_intake_answers when done.",
            intake_form = intakeForm,
        });
    }

    private async Task<string> SaveIntakeAnswersAsync(JsonDocument input, CancellationToken ct)
    {
        var sessionId   = Guid.Parse(RequireString(input, "session_id"));
        var answersText = RequireString(input, "answers_json");

        // Accept either raw JSON or plain text answers — wrap plain text in JSON
        string answersJson;
        try
        {
            JsonDocument.Parse(answersText);
            answersJson = answersText;
        }
        catch
        {
            answersJson = JsonSerializer.Serialize(new { raw_answers = answersText });
        }

        var doc = JsonDocument.Parse(answersJson);
        await scaffoldingRepo.UpdateIntakeAnswersAsync(sessionId, doc);

        // Also store raw text on session for plan service parsing
        await scaffoldingRepo.UpdateIntakeAnswersAsync(sessionId,
            JsonDocument.Parse(JsonSerializer.Serialize(new { raw = answersText })));

        return Ok(new
        {
            session_id = sessionId,
            message    = "Intake answers saved. Call present_scaffolding_plan to review the plan before approving.",
        });
    }

    private async Task<string> PresentScaffoldingPlanAsync(JsonDocument input, CancellationToken ct)
    {
        var sessionId = Guid.Parse(RequireString(input, "session_id"));
        var plan      = await scaffoldingPlan.BuildPlanAsync(sessionId, ct);
        return Ok(new
        {
            session_id = sessionId,
            plan,
            message = "Review the plan above. Call approve_scaffolding with the session_id to proceed.",
        });
    }

    private async Task<string> ApproveScaffoldingAsync(JsonDocument input, CancellationToken ct)
    {
        var sessionId = Guid.Parse(RequireString(input, "session_id"));
        await scaffoldingRepo.ApproveAsync(sessionId);

        logger.LogInformation("Scaffolding approved for session {Id}, starting scaffold", sessionId);
        var result = await scaffolding.ScaffoldAsync(sessionId, ct);

        return result.Success
            ? Ok(new
              {
                  success    = true,
                  agent_name = result.AgentName,
                  port       = result.Port,
                  message    = $"{result.AgentName} scaffolded successfully on port {result.Port}.",
              })
            : Err($"Scaffolding failed for {result.AgentName}: {result.ErrorMessage}");
    }

    private async Task<string> ListScaffoldedAgentsAsync(JsonDocument input, CancellationToken ct)
    {
        var limit = GetInt(input, "limit") ?? 10;
        var rows  = await scaffoldedRepo.GetRecentAsync(limit);
        return Ok(new { count = rows.Count(), agents = rows });
    }

    private async Task<string> ListPortRegistryAsync(CancellationToken ct)
    {
        var ports = await portRegistry.GetAllAsync();
        return Ok(new { count = ports.Count(), ports });
    }

    // ── Agent Lifecycle ────────────────────────────────────────────────────────

    private async Task<string> GetAgentInfoAsync(JsonDocument input, CancellationToken ct)
    {
        var agentName = RequireString(input, "agent_name");
        var info      = await agentMetadata.GetAgentInfoAsync(agentName);
        return info == null ? Err($"Agent '{agentName}' not found.") : Ok(info);
    }

    private async Task<string> UpdateAgentMetadataAsync(JsonDocument input, CancellationToken ct)
    {
        var agentName = RequireString(input, "agent_name");
        var field     = RequireString(input, "field");
        var value     = RequireString(input, "value");

        var (success, message) = await agentMetadata.UpdateMetadataAsync(agentName, field, value, ct);
        return success ? Ok(new { success = true, agent_name = agentName, field, value, message })
                       : Err(message);
    }

    private async Task<string> PlanAgentCodeUpdateAsync(JsonDocument input, CancellationToken ct)
    {
        var agentName   = RequireString(input, "agent_name");
        var changeDesc  = RequireString(input, "change_description");
        var plan        = await codeUpdate.PlanCodeUpdateAsync(agentName, changeDesc, ct);
        return Ok(new
        {
            agent_name = agentName,
            plan,
            message = "Review the plan above. Call execute_agent_code_update to proceed.",
        });
    }

    private async Task<string> ExecuteAgentCodeUpdateAsync(JsonDocument input, CancellationToken ct)
    {
        var agentName      = RequireString(input, "agent_name");
        var planSummary    = RequireString(input, "plan_summary");
        var filesToModify  = input.RootElement.TryGetProperty("files_to_modify", out var filesEl)
            ? filesEl.EnumerateArray().Select(e => e.GetString()!).ToArray()
            : Array.Empty<string>();

        // Confirmation gate for hand-built agents
        if (!await IsScaffoldedAgent(agentName))
        {
            var confirmKey = $"code_update_confirm:{agentName.ToLower()}";
            var db2        = redis.GetDatabase();
            var confirmed  = await db2.KeyExistsAsync(confirmKey);
            if (!confirmed)
            {
                await db2.StringSetAsync(confirmKey, "1", TimeSpan.FromMinutes(5));
                return Ok(new
                {
                    requires_confirm = true,
                    message = $"⚠️ {agentName} is a hand-built agent. This will modify its source code and restart the container. Call execute_agent_code_update again within 5 minutes to confirm.",
                    agent_name = agentName,
                });
            }
            await db2.KeyDeleteAsync(confirmKey);
        }

        var result = await codeUpdate.ExecuteCodeUpdateAsync(agentName, planSummary, filesToModify, ct);
        return result.Success
            ? Ok(new { success = true, agent_name = agentName, commit_sha = result.CommitSha, message = $"Code update complete. Commit: {result.CommitSha}" })
            : Err($"Code update failed: {result.ErrorMessage}");
    }

    private async Task<string> SoftRetireAgentAsync(JsonDocument input, CancellationToken ct)
    {
        var agentName = RequireString(input, "agent_name");
        var confirm   = GetBool(input, "confirm") ?? false;

        if (!await IsScaffoldedAgent(agentName) && !confirm)
        {
            return Ok(new
            {
                requires_confirm = true,
                message = $"⚠️ {agentName} is a hand-built agent. Set confirm=true to proceed with soft-retire.",
                agent_name = agentName,
            });
        }

        var (success, message) = await scaffolding.SoftRetireAsync(agentName, ct);
        return success ? Ok(new { success = true, agent_name = agentName, message })
                       : Err(message);
    }

    private async Task<string> HardRetireAgentAsync(JsonDocument input, CancellationToken ct)
    {
        var agentName  = RequireString(input, "agent_name");
        var dropSchema = GetBool(input, "drop_schema") ?? false;
        var confirm    = GetBool(input, "confirm") ?? false;

        // Protect core agents unconditionally
        var coreAgents = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "rex", "jarvis", "andrew" };
        if (coreAgents.Contains(agentName))
            return Err($"Hard-retiring {agentName} is not permitted. Escalate to Gert if this is truly required.");

        // Always require explicit confirm=true
        if (!confirm)
        {
            return Ok(new
            {
                requires_confirm = true,
                message = $"⚠️ Hard-retiring {agentName} will archive its source and{(dropSchema ? " drop its database schema and" : "")} remove it from docker-compose. This is irreversible. Repeat the call with confirm=true to proceed.",
                agent_name  = agentName,
                drop_schema = dropSchema,
            });
        }

        var (success, message) = await scaffolding.HardRetireAsync(agentName, dropSchema, ct);
        return success ? Ok(new { success = true, agent_name = agentName, message })
                       : Err(message);
    }

    private async Task<string> ReactivateAgentAsync(JsonDocument input, CancellationToken ct)
    {
        var agentName    = RequireString(input, "agent_name");
        var (success, message) = await scaffolding.ReactivateAsync(agentName, ct);
        return success ? Ok(new { success = true, agent_name = agentName, message })
                       : Err(message);
    }

    private async Task<string> ListAgentsAsync(JsonDocument input, CancellationToken ct)
    {
        var includeRetired = GetBool(input, "include_retired") ?? false;
        var agents         = await agentMetadata.ListAgentsAsync(includeRetired);
        return Ok(new { count = agents.Count(), agents });
    }

    private async Task<bool> IsScaffoldedAgent(string agentName)
    {
        try
        {
            var info = await agentMetadata.GetAgentInfoAsync(agentName);
            if (info is null) return false;
            // info is an anonymous object — check via JSON
            var json = JsonSerializer.Serialize(info);
            return json.Contains("\"was_scaffolded\":true", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string RequireString(JsonDocument doc, string key)
    {
        if (!doc.RootElement.TryGetProperty(key, out var prop))
            throw new ArgumentException($"Required parameter '{key}' is missing.");
        return prop.GetString() ?? throw new ArgumentException($"Parameter '{key}' must not be null.");
    }

    private static string? GetString(JsonDocument doc, string key) =>
        doc.RootElement.TryGetProperty(key, out var p) ? p.GetString() : null;

    private static int? GetInt(JsonDocument doc, string key) =>
        doc.RootElement.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Number
            ? p.GetInt32() : null;

    // ── Tester Agent ───────────────────────────────────────────────────────────

    private async Task<string> SaveTestSpecAsync(JsonDocument input, CancellationToken ct)
    {
        var appName    = RequireString(input, "app_name");
        var specJson   = RequireString(input, "spec_json");
        var description = input.RootElement.TryGetProperty("description", out var d) ? d.GetString() : null;

        // Validate the spec JSON is valid
        try { JsonDocument.Parse(specJson); }
        catch { return Err("spec_json is not valid JSON."); }

        var key = $"test_spec:{appName}";
        var storedValue = System.Text.Json.JsonSerializer.Serialize(new { appName, description, specJson });
        await memory.RememberFactAsync(key, storedValue, ct);

        return Ok(new { app_name = appName, saved = true, message = $"Test spec for '{appName}' saved." });
    }

    private async Task<string> GetTestSpecAsync(JsonDocument input, CancellationToken ct)
    {
        var appName = RequireString(input, "app_name");
        var facts   = await memory.LoadFactsAsync(ct);
        var key     = $"test_spec:{appName}";

        if (!facts.TryGetValue(key, out var value))
            return Err($"No test spec found for '{appName}'.");

        return Ok(new { app_name = appName, spec = value });
    }

    private async Task<string> ListTestSpecsAsync(CancellationToken ct)
    {
        var facts = await memory.LoadFactsAsync(ct);
        var specs  = facts
            .Where(kv => kv.Key.StartsWith("test_spec:"))
            .Select(kv =>
            {
                var appName = kv.Key["test_spec:".Length..];
                try
                {
                    var parsed = JsonDocument.Parse(kv.Value).RootElement;
                    return new
                    {
                        app_name    = appName,
                        description = parsed.TryGetProperty("description", out var d) ? d.GetString() : null
                    };
                }
                catch { return new { app_name = appName, description = (string?)null }; }
            })
            .ToList();

        return Ok(new { count = specs.Count, specs });
    }

    private async Task<string> RunTestSuiteAsync(JsonDocument input, CancellationToken ct)
    {
        var appName    = RequireString(input, "app_name");
        var phase      = RequireString(input, "phase");
        var snapshotPath = input.RootElement.TryGetProperty("before_snapshot_path", out var sp)
            ? sp.GetString() : null;

        // Load saved test spec
        var facts = await memory.LoadFactsAsync(ct);
        var key   = $"test_spec:{appName}";
        if (!facts.TryGetValue(key, out var specEntry))
            return Err($"No test spec found for '{appName}'. Call save_test_spec first.");

        string specJson;
        try
        {
            var parsed = JsonDocument.Parse(specEntry).RootElement;
            specJson   = parsed.GetProperty("specJson").GetString()!;
        }
        catch
        {
            return Err("Saved test spec is malformed.");
        }

        logger.LogInformation("[Rex] Running test suite for '{App}' — phase: {Phase}", appName, phase);
        var report = await testerAgent.RunTestsAsync(appName, phase, specJson, snapshotPath, ct);
        return Ok(new { app_name = appName, phase, report });
    }

    // ── Deployment Recipes ─────────────────────────────────────────────────────

    private async Task<string> SaveDeploymentRecipeAsync(JsonDocument input, CancellationToken ct)
    {
        var appName       = RequireString(input, "app_name");
        var targetServer  = RequireString(input, "target_server");
        var stepsJson     = RequireString(input, "steps_json");
        var description   = input.RootElement.TryGetProperty("description", out var d)   ? d.GetString()  : null;
        var preChecksJson = input.RootElement.TryGetProperty("pre_checks_json", out var pc) ? pc.GetString() : null;
        var postChecksJson = input.RootElement.TryGetProperty("post_checks_json", out var poc) ? poc.GetString() : null;

        try { JsonDocument.Parse(stepsJson); }
        catch { return Err("steps_json is not valid JSON."); }

        await deploymentRecipes.UpsertAsync(appName, description, targetServer, stepsJson, preChecksJson, postChecksJson);
        return Ok(new { app_name = appName, saved = true, message = $"Deployment recipe for '{appName}' saved." });
    }

    private async Task<string> GetDeploymentRecipeAsync(JsonDocument input, CancellationToken ct)
    {
        var appName = RequireString(input, "app_name");
        var recipe  = await deploymentRecipes.GetByAppNameAsync(appName);
        if (recipe is null) return Err($"No deployment recipe found for '{appName}'.");
        return Ok(recipe);
    }

    private async Task<string> ListDeploymentRecipesAsync(CancellationToken ct)
    {
        var recipes = (await deploymentRecipes.GetAllAsync()).ToList();
        return Ok(new { count = recipes.Count, recipes });
    }

    private async Task<string> ExecuteDeploymentAsync(JsonDocument input, CancellationToken ct)
    {
        var appName   = RequireString(input, "app_name");
        var confirmed = GetBool(input, "confirmed") ?? false;

        if (!confirmed)
            return Err($"Deployment of '{appName}' requires confirmed=true. Ask Gert for approval first.");

        var recipe = await deploymentRecipes.GetByAppNameAsync(appName);
        if (recipe is null) return Err($"No deployment recipe found for '{appName}'.");

        var stepsJson = (string)recipe.StepsJson;
        JsonElement steps;
        try { steps = JsonDocument.Parse(stepsJson).RootElement; }
        catch { return Err("Deployment recipe steps are malformed JSON."); }

        var results = new List<object>();
        var allPassed = true;

        foreach (var step in steps.EnumerateArray())
        {
            var type = step.TryGetProperty("type", out var t) ? t.GetString() : null;
            var sw   = System.Diagnostics.Stopwatch.StartNew();

            object stepResult;
            try
            {
                stepResult = type switch
                {
                    "ssh_exec" => await ExecuteSshStepAsync(step, ct),
                    "container_restart" => await ExecuteContainerRestartStepAsync(step, ct),
                    "container_build"   => await ExecuteContainerBuildStepAsync(step, ct),
                    "wait"              => await ExecuteWaitStepAsync(step),
                    "http_check"        => await ExecuteHttpCheckStepAsync(step, ct),
                    _ => new { type, success = false, error = $"Unknown step type '{type}'" }
                };
            }
            catch (Exception ex)
            {
                stepResult = new { type, success = false, error = ex.Message };
            }

            sw.Stop();
            // Check success by serializing and checking the JSON
            var stepJson = JsonSerializer.Serialize(stepResult);
            var stepDoc  = JsonDocument.Parse(stepJson);
            var stepOk   = stepDoc.RootElement.TryGetProperty("success", out var suc) && suc.GetBoolean();
            if (!stepOk) allPassed = false;

            results.Add(new { step = step.ToString(), result = stepResult, duration_ms = sw.ElapsedMilliseconds });
        }

        logger.LogInformation("[Rex] Deployment of '{App}' completed — all_passed: {Ok}", appName, allPassed);
        return Ok(new { app_name = appName, all_passed = allPassed, steps = results });
    }

    private async Task<object> ExecuteSshStepAsync(JsonElement step, CancellationToken ct)
    {
        var server  = step.GetProperty("server").GetString()!;
        var command = step.GetProperty("command").GetString()!;

        var creds = await ssh.GetCredentialsAsync(server, ct);
        if (creds is null) return new { success = false, error = $"No SSH credentials for '{server}'" };

        var target = new ConnectionTarget(server, null, 22, OsType.Linux);
        var output = await ssh.RunAndReadAsync(target, creds, command, SshPermission.ReadWrite, ct);
        return new { success = output is not null, output = output ?? "command failed" };
    }

    private async Task<object> ExecuteContainerRestartStepAsync(JsonElement step, CancellationToken ct)
    {
        var name = step.GetProperty("name").GetString()!;
        await containers.RestartContainerAsync(name, ct);
        return new { success = true };
    }

    private async Task<object> ExecuteContainerBuildStepAsync(JsonElement step, CancellationToken ct)
    {
        var dockerfile  = step.GetProperty("dockerfile").GetString()!;
        var contextPath = step.GetProperty("context_path").GetString()!;
        var tag         = step.GetProperty("tag").GetString()!;
        var (ok, output) = await containers.BuildImageAsync(dockerfile, contextPath, tag, ct);
        return new { success = ok, output, error = ok ? null : "Build failed — see output" };
    }

    private static Task<object> ExecuteWaitStepAsync(JsonElement step)
    {
        var seconds = step.TryGetProperty("seconds", out var s) ? s.GetInt32() : 5;
        Thread.Sleep(TimeSpan.FromSeconds(seconds));
        return Task.FromResult<object>(new { success = true, waited_seconds = seconds });
    }

    private async Task<object> ExecuteHttpCheckStepAsync(JsonElement step, CancellationToken ct)
    {
        var url            = step.GetProperty("url").GetString()!;
        var expectStatus   = step.TryGetProperty("expect_status", out var es) ? es.GetInt32() : 200;
        var timeoutSeconds = step.TryGetProperty("timeout_seconds", out var ts) ? ts.GetInt32() : 10;

        var result = await http.CheckAsync(url, timeoutSeconds, followRedirects: true, ct);
        if (!result.Success)
            return new { success = false, status_code = (int?)null, response_time_ms = 0L, error = result.ErrorMessage };

        var r       = result.Value!;
        var matches = r.StatusCode.HasValue && r.StatusCode.Value == expectStatus;
        return new
        {
            success          = matches,
            status_code      = r.StatusCode,
            expected         = expectStatus,
            response_time_ms = r.ResponseTimeMs,
            error            = matches ? null : $"Expected HTTP {expectStatus}, got {r.StatusCode?.ToString() ?? "no response"}"
        };
    }

    private static bool? GetBool(JsonDocument doc, string key) =>
        doc.RootElement.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.True ||
        doc.RootElement.TryGetProperty(key, out p) && p.ValueKind == JsonValueKind.False
            ? p.GetBoolean() : null;

    private static Dictionary<string, string> GetContextFiles(JsonDocument doc)
    {
        if (!doc.RootElement.TryGetProperty("context_files", out var cfEl))
            return [];

        return cfEl.EnumerateObject()
            .Where(p => p.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(p => p.Name, p => p.Value.GetString()!);
    }

    private static string ShellOk(Models.ShellResult r) =>
        Ok(new { success = r.Success, exit_code = r.ExitCode, stdout = r.Stdout, stderr = r.Stderr });

    private static string Ok(object value)  => JsonSerializer.Serialize(value, JsonOpts);
    private static string Err(string msg)   => JsonSerializer.Serialize(new { error = msg }, JsonOpts);
}
