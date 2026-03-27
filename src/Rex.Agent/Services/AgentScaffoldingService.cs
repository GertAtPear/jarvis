using System.Text.Json;
using Dapper;
using Mediahost.Agents.Data;
using Rex.Agent.Data.Repositories;

namespace Rex.Agent.Services;

public record ScaffoldingResult(
    bool    Success,
    string  AgentName,
    int     Port,
    string? ErrorMessage = null);

public class AgentScaffoldingService(
    ScaffoldingSessionRepository sessions,
    ScaffoldedAgentRepository    scaffoldedAgents,
    PortRegistryRepository       portRegistry,
    DeveloperAgentService        devAgent,
    ContainerService             containers,
    DbConnectionFactory          db,
    IConfiguration               config,
    ILogger<AgentScaffoldingService> logger)
{
    private string ProjectRoot    => config["Rex:ProjectRoot"]    ?? "/project";
    private string ScaffoldingRoot => config["Rex:ScaffoldingRoot"] ?? "/scaffolding";

    // ── Scaffold ──────────────────────────────────────────────────────────────

    public async Task<ScaffoldingResult> ScaffoldAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await sessions.GetByIdAsync(sessionId)
            ?? throw new InvalidOperationException($"Session {sessionId} not found.");

        if (session.Status != "approved")
            throw new InvalidOperationException($"Session must be approved before scaffolding. Current status: {session.Status}");

        var agentName  = session.AgentName ?? throw new InvalidOperationException("Agent name not set on session.");
        var port       = session.AssignedPort ?? throw new InvalidOperationException("Port not assigned.");
        var department = session.Department ?? "General";
        var agentNameL = agentName.ToLower();
        var schemaName = agentNameL + "_schema";

        await sessions.UpdateStatusAsync(sessionId, "scaffolding");

        // Create scaffolded_agents tracking row
        var logId = await scaffoldedAgents.LogAsync(new ScaffoldedAgentLog
        {
            SessionId    = sessionId,
            AgentName    = agentName,
            Port         = port,
            Department   = department,
            FilesCreated = "[]",
        });

        var filesCreated = new List<string>();

        try
        {
            // Determine feature flags from intake answers
            var intake      = session.IntakeAnswers ?? "";
            var hasSsh      = intake.Contains("ssh", StringComparison.OrdinalIgnoreCase);
            var hasJobs     = intake.Contains("schedul", StringComparison.OrdinalIgnoreCase) ||
                              intake.Contains("cron", StringComparison.OrdinalIgnoreCase);
            var isStateful  = !intake.Contains("stateless", StringComparison.OrdinalIgnoreCase);
            var baseClass   = isStateful ? "BaseAgentService" : "AgentBase";

            var tokens = BuildTokenMap(agentName, port, department, schemaName, baseClass, intake, session.ProposedTools);
            var flags  = BuildFlagSet(hasSsh, hasJobs, isStateful, hasJobs);

            // ── 1. Generate source files ────────────────────────────────────────

            var agentDir = Path.Combine(ProjectRoot, "src", $"{agentName}.Agent");
            Directory.CreateDirectory(agentDir);

            var filesToGenerate = new[]
            {
                ("agent.csproj.tmpl",      $"{agentName}.Agent.csproj"),
                ("Dockerfile.tmpl",         "Dockerfile"),
                ("Program.cs.tmpl",         "Program.cs"),
                ("SystemPrompt.tmpl",       $"SystemPrompts/{agentName}SystemPrompt.cs"),
                ("AgentService.tmpl",       $"Services/{agentName}AgentService.cs"),
                ("ToolDefinitions.tmpl",    $"Tools/{agentName}ToolDefinitions.cs"),
                ("ToolExecutor.tmpl",       $"Tools/{agentName}ToolExecutor.cs"),
                ("Controller.tmpl",         $"Controllers/{agentName}Endpoints.cs"),
                ("ServiceExtensions.tmpl",  $"Extensions/{agentName}ServiceExtensions.cs"),
            };

            foreach (var (tmplFile, outputRel) in filesToGenerate)
            {
                var template   = TemplateEngine.ReadTemplate(ScaffoldingRoot, "templates/" + tmplFile);
                var rendered   = TemplateEngine.Render(template, tokens, flags);
                var outputPath = Path.Combine(agentDir, outputRel);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                await File.WriteAllTextAsync(outputPath, rendered, ct);
                filesCreated.Add(outputPath);
                logger.LogInformation("Generated {File}", outputRel);
            }

            // ── 2. Create agent schema ──────────────────────────────────────────

            var schemaSql = TemplateEngine.Render(
                TemplateEngine.ReadTemplate(ScaffoldingRoot, "templates/schema.sql.tmpl"),
                tokens, flags);

            await ExecuteSqlAsync(schemaSql, ct);
            logger.LogInformation("Schema created for {Agent}", agentName);

            await scaffoldedAgents.UpdateBuildResultAsync(logId,
                buildSuccess: false, healthPassed: false, registered: false,
                smokeTestResponse: null, errorDetails: null);

            // ── 3. Patch docker-compose.yml ─────────────────────────────────────

            var composePath = Path.Combine(ProjectRoot, "docker-compose.yml");
            var composeSnippet = TemplateEngine.Render(
                TemplateEngine.ReadTemplate(ScaffoldingRoot, "templates/compose-service.tmpl"),
                tokens, flags);

            var composeContent = await File.ReadAllTextAsync(composePath, ct);
            if (!composeContent.Contains($"{agentNameL}-agent:"))
            {
                composeContent += "\n" + composeSnippet;
                await File.WriteAllTextAsync(composePath, composeContent, ct);
                filesCreated.Add(composePath);
                logger.LogInformation("Patched docker-compose.yml for {Agent}", agentName);
            }

            // ── 4. Git commit ───────────────────────────────────────────────────

            await RunProcessAsync("git",
                $"-C {ProjectRoot} add -A", ct);
            await RunProcessAsync("git",
                $"-C {ProjectRoot} commit -m \"feat: scaffold {agentName} agent (port {port})\"", ct);
            await RunProcessAsync("git",
                $"-C {ProjectRoot} push", ct);
            logger.LogInformation("Committed and pushed scaffold for {Agent}", agentName);

            // ── 5. Build container ──────────────────────────────────────────────

            var (buildOk, buildOutput) = await containers.BuildImageAsync(
                Path.Combine("src", $"{agentName}.Agent", "Dockerfile"),
                ProjectRoot,
                $"{agentNameL}-agent:latest",
                ct);

            if (!buildOk)
            {
                await sessions.UpdateStatusAsync(sessionId, "failed");
                await scaffoldedAgents.UpdateBuildResultAsync(logId,
                    buildSuccess: false, healthPassed: false, registered: false,
                    smokeTestResponse: null, errorDetails: buildOutput[^Math.Min(500, buildOutput.Length)..]);
                return new ScaffoldingResult(false, agentName, port,
                    $"Container build failed:\n{buildOutput[^Math.Min(500, buildOutput.Length)..]}");
            }

            // ── 6. Start container ──────────────────────────────────────────────

            await RunProcessAsync("podman",
                $"compose -f {Path.Combine(ProjectRoot, "docker-compose.yml")} up -d {agentNameL}-agent", ct);

            // Poll health for up to 60s
            var healthOk = await PollHealthAsync(port, 12, 5000, ct);

            // ── 7. Register in Jarvis ───────────────────────────────────────────

            bool registered = false;
            try
            {
                await RegisterAgentAsync(agentName, port, department, ct);
                registered = true;
                logger.LogInformation("Registered {Agent} in Jarvis", agentName);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to register {Agent} in Jarvis", agentName);
            }

            // ── 8. Smoke test ───────────────────────────────────────────────────

            string? smokeResponse = null;
            try
            {
                using var http = new HttpClient();
                var smokeResult = await http.PostAsJsonAsync(
                    $"http://localhost:{port}/api/{agentNameL}/chat",
                    new { message = "Hello, can you confirm you are operational?", sessionId = "smoke-test" },
                    ct);
                smokeResponse = await smokeResult.Content.ReadAsStringAsync(ct);
                smokeResponse = smokeResponse[..Math.Min(300, smokeResponse.Length)];
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Smoke test failed for {Agent}", agentName);
                smokeResponse = ex.Message;
            }

            await sessions.UpdateStatusAsync(sessionId, "complete");
            await scaffoldedAgents.UpdateBuildResultAsync(logId,
                buildSuccess: true, healthPassed: healthOk, registered: registered,
                smokeTestResponse: smokeResponse, errorDetails: null);

            return new ScaffoldingResult(true, agentName, port);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scaffolding failed for session {Session}", sessionId);
            await sessions.UpdateStatusAsync(sessionId, "failed");
            await scaffoldedAgents.UpdateBuildResultAsync(logId,
                buildSuccess: false, healthPassed: false, registered: false,
                smokeTestResponse: null, errorDetails: ex.Message);
            return new ScaffoldingResult(false, agentName, port, ex.Message);
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public async Task<(bool Success, string Message)> SoftRetireAsync(
        string agentName, CancellationToken ct = default)
    {
        var agentNameL = agentName.ToLower();
        try
        {
            await using var conn = db.Create();
            await conn.ExecuteAsync("""
                UPDATE jarvis_schema.agents
                SET status = 'retired', retired_at = NOW(), updated_at = NOW()
                WHERE LOWER(name) = LOWER(@agentName)
                """, new { agentName });

            // Find and deactivate port
            var port = await conn.ExecuteScalarAsync<int?>(
                "SELECT port FROM rex_schema.port_registry WHERE LOWER(agent_name) = LOWER(@agentName)",
                new { agentName });
            if (port.HasValue)
                await portRegistry.DeactivateAsync(port.Value);

            await RunProcessAsync("podman",
                $"compose -f /project/docker-compose.yml stop {agentNameL}-agent", ct);

            logger.LogInformation("Soft-retired {Agent}", agentName);
            return (true, $"{agentName} has been soft-retired. Container stopped, port deactivated.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Soft retire failed for {Agent}", agentName);
            return (false, ex.Message);
        }
    }

    public async Task<(bool Success, string Message)> HardRetireAsync(
        string agentName, bool dropSchema, CancellationToken ct = default)
    {
        var agentNameL = agentName.ToLower();
        var schemaName = agentNameL + "_schema";
        try
        {
            // Stop and remove container
            await RunProcessAsync("podman",
                $"compose -f {Path.Combine(ProjectRoot, "docker-compose.yml")} stop {agentNameL}-agent", ct);
            await RunProcessAsync("podman",
                $"compose -f {Path.Combine(ProjectRoot, "docker-compose.yml")} rm -f {agentNameL}-agent", ct);

            // Remove compose block from docker-compose.yml
            var composePath = Path.Combine(ProjectRoot, "docker-compose.yml");
            await RemoveComposeBlockAsync(composePath, agentNameL);

            // Archive source directory
            var srcDir     = Path.Combine(ProjectRoot, "src", $"{agentName}.Agent");
            var archiveDir = Path.Combine(ProjectRoot, "archive", $"{agentName}.Agent");
            if (Directory.Exists(srcDir))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(archiveDir)!);
                Directory.Move(srcDir, archiveDir);
            }

            // Drop schema if requested
            if (dropSchema)
                await ExecuteSqlAsync($"DROP SCHEMA IF EXISTS {schemaName} CASCADE;", ct);

            // Update Jarvis registry
            await using var conn = db.Create();
            await conn.ExecuteAsync("""
                DELETE FROM jarvis_schema.agents WHERE LOWER(name) = LOWER(@agentName)
                """, new { agentName });

            // Commit and push
            await RunProcessAsync("git",
                $"-C {ProjectRoot} add -A", ct);
            await RunProcessAsync("git",
                $"-C {ProjectRoot} commit -m \"chore: hard retire {agentName} agent\"", ct);
            await RunProcessAsync("git",
                $"-C {ProjectRoot} push", ct);

            return (true, $"{agentName} has been hard-retired. Source archived, compose block removed{(dropSchema ? ", schema dropped" : "")}.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Hard retire failed for {Agent}", agentName);
            return (false, ex.Message);
        }
    }

    public async Task<(bool Success, string Message)> ReactivateAsync(
        string agentName, CancellationToken ct = default)
    {
        var agentNameL = agentName.ToLower();
        try
        {
            // Check source exists
            var srcDir = Path.Combine(ProjectRoot, "src", $"{agentName}.Agent");
            if (!Directory.Exists(srcDir))
                return (false, $"Source directory not found: {srcDir}. Cannot reactivate a hard-retired agent.");

            // Update statuses
            await using var conn = db.Create();
            await conn.ExecuteAsync("""
                UPDATE jarvis_schema.agents
                SET status = 'active', retired_at = NULL, updated_at = NOW()
                WHERE LOWER(name) = LOWER(@agentName)
                """, new { agentName });

            var port = await conn.ExecuteScalarAsync<int?>(
                "SELECT port FROM rex_schema.port_registry WHERE LOWER(agent_name) = LOWER(@agentName)",
                new { agentName });
            if (port.HasValue)
                await using (var conn2 = db.Create())
                    await conn2.ExecuteAsync(
                        "UPDATE rex_schema.port_registry SET is_active = true WHERE port = @port",
                        new { port = port.Value });

            // Start container
            await RunProcessAsync("podman",
                $"compose -f {Path.Combine(ProjectRoot, "docker-compose.yml")} up -d {agentNameL}-agent", ct);

            var healthOk = port.HasValue && await PollHealthAsync(port.Value, 12, 5000, ct);

            return healthOk
                ? (true, $"{agentName} reactivated successfully. Health check passed.")
                : (true, $"{agentName} reactivated (container started but health check did not pass within 60s).");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Reactivation failed for {Agent}", agentName);
            return (false, ex.Message);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Dictionary<string, string> BuildTokenMap(
        string agentName, int port, string department, string schemaName,
        string baseClass, string intake, string? proposedToolsJson)
    {
        var agentNameL = agentName.ToLower();
        var tools      = ParseProposedTools(proposedToolsJson);
        var toolDefs   = BuildToolDefinitions(tools);
        var toolCases  = BuildToolCases(tools);
        var agentRole  = ExtractField(intake, "Purpose", "AI Agent");

        return new Dictionary<string, string>
        {
            ["AgentName"]          = agentName,
            ["AgentNameLower"]     = agentNameL,
            ["Port"]               = port.ToString(),
            ["Department"]         = department,
            ["BaseClass"]          = baseClass,
            ["SchemaName"]         = schemaName,
            ["AgentRole"]          = agentRole,
            ["AgentRoleDescription"] = agentRole,
            ["ToolDefinitions"]    = toolDefs,
            ["ToolCases"]          = toolCases,
            ["RoutingKeywords"]    = ExtractField(intake, "Routing Keywords", agentNameL),
            ["IsStateful"]         = baseClass == "BaseAgentService" ? "true" : "",
            ["IsStateless"]        = baseClass == "AgentBase" ? "true" : "",
            ["KnowledgeStoreDescription"] = "",
            ["AgentSpecificTables"] = "",
        };
    }

    private static HashSet<string> BuildFlagSet(bool hasSsh, bool hasJobs, bool isStateful, bool hasAgentSpecificTables)
    {
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (hasSsh)                  flags.Add("HasSsh");
        if (hasJobs)                 flags.Add("HasJobs");
        if (isStateful)              flags.Add("IsStateful");
        if (!isStateful)             flags.Add("IsStateless");
        if (hasAgentSpecificTables)  flags.Add("HasAgentSpecificTables");
        return flags;
    }

    private static List<(string Name, string Description)> ParseProposedTools(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            var doc  = JsonDocument.Parse(json);
            var list = new List<(string, string)>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var desc = el.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(name)) list.Add((name, desc));
            }
            return list;
        }
        catch { return []; }
    }

    private static string BuildToolDefinitions(List<(string Name, string Description)> tools)
    {
        const string emptySchema = "{\"type\":\"object\",\"properties\":{}}";

        if (tools.Count == 0)
            return $"new ToolDefinition(\n            \"ping\",\n            \"Respond with a simple status message\",\n            JsonDocument.Parse(\"{emptySchema}\"))";

        return string.Join(",\n\n        ", tools.Select(t =>
            $"new ToolDefinition(\n            \"{t.Name}\",\n            \"{t.Description.Replace("\"", "\\\"")}\",\n            JsonDocument.Parse(\"{emptySchema}\"))"));
    }

    private static string BuildToolCases(List<(string Name, string Description)> tools)
    {
        if (tools.Count == 0) return "";
        return string.Join("\n                ", tools.Select(t =>
            $"\"{t.Name}\" => await Execute{ToPascalCase(t.Name)}Async(input, ct),"));
    }

    private static string ToPascalCase(string name) =>
        string.Concat(name.Split('_').Select(p =>
            p.Length > 0 ? char.ToUpper(p[0]) + p[1..] : ""));

    private static string ExtractField(string text, string fieldName, string defaultValue)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(fieldName + ":", StringComparison.OrdinalIgnoreCase))
                return trimmed.Split(':', 2).Last().Trim();
        }
        return defaultValue;
    }

    private async Task ExecuteSqlAsync(string sql, CancellationToken ct)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync(sql);
    }

    private async Task RegisterAgentAsync(string agentName, int port, string department, CancellationToken ct)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync("""
            INSERT INTO jarvis_schema.agents
                (name, display_name, department_id, description, base_url,
                 status, was_scaffolded, capabilities, routing_keywords)
            SELECT @name, @name,
                   (SELECT id FROM jarvis_schema.departments WHERE name = @department),
                   @name || ' — scaffolded agent',
                   'http://' || @agentNameL || '-agent:' || @port,
                   'active', true, '[]'::jsonb, '[]'::jsonb
            ON CONFLICT (name) DO UPDATE SET
                status       = 'active',
                retired_at   = NULL,
                updated_at   = NOW()
            """, new { name = agentName, agentNameL = agentName.ToLower(), port, department });
    }

    private async Task<bool> PollHealthAsync(int port, int attempts, int delayMs, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        for (var i = 0; i < attempts; i++)
        {
            try
            {
                var resp = await http.GetAsync($"http://localhost:{port}/health", ct);
                if (resp.IsSuccessStatusCode) return true;
            }
            catch { /* not ready yet */ }

            if (i < attempts - 1)
                await Task.Delay(delayMs, ct);
        }
        return false;
    }

    private static async Task RemoveComposeBlockAsync(string composePath, string agentNameL)
    {
        var content = await File.ReadAllTextAsync(composePath);
        // Find the service block header and remove until the next service block or end
        var marker    = $"\n  {agentNameL}-agent:";
        var startIdx  = content.IndexOf(marker, StringComparison.Ordinal);
        if (startIdx < 0) return;

        // Find next service block (next "  <name>-agent:" or "  <name>:" at same indent)
        var nextService = System.Text.RegularExpressions.Regex.Match(
            content, @"\n  \w[\w-]+:", 0);
        // Scan past startIdx
        var endIdx     = content.Length;
        var searchFrom = startIdx + marker.Length;
        var m = System.Text.RegularExpressions.Regex.Match(
            content[searchFrom..], @"\n  \w[\w-]+:");
        if (m.Success) endIdx = searchFrom + m.Index;

        content = content[..startIdx] + content[endIdx..];
        await File.WriteAllTextAsync(composePath, content);
    }

    private static async Task<(bool Success, string Output)> RunProcessAsync(
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
