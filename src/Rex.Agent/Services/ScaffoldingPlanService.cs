using System.Text.Json;
using Rex.Agent.Data.Repositories;

namespace Rex.Agent.Services;

public class ScaffoldingPlanService(
    ScaffoldingSessionRepository sessions,
    PortRegistryRepository portRegistry,
    DeveloperAgentService devAgent,
    IConfiguration config,
    ILogger<ScaffoldingPlanService> logger)
{
    private string ScaffoldingRoot =>
        config["Rex:ScaffoldingRoot"] ?? "/scaffolding";

    public async Task<string> BuildPlanAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await sessions.GetByIdAsync(sessionId)
            ?? throw new InvalidOperationException($"Session {sessionId} not found.");

        if (string.IsNullOrEmpty(session.IntakeAnswers))
            throw new InvalidOperationException("Intake answers are required before building a plan.");

        // Assign port
        var agentName = ExtractAgentName(session.IntakeAnswers);
        await sessions.UpdateAgentNameAsync(sessionId, agentName);

        var port = await portRegistry.AssignNextPortAsync(agentName);
        await sessions.UpdateAssignedPortAsync(sessionId, port);

        // Sub-agent: propose tools
        var toolProposalPrompt = TemplateEngine.ReadTemplate(
            ScaffoldingRoot, "intake/tool-proposal-prompt.md");

        var proposedToolsJson = await devAgent.DevelopFileAsync(
            $"Intake answers:\n{session.IntakeAnswers}",
            "proposed-tools.json",
            new Dictionary<string, string> { ["system-prompt"] = toolProposalPrompt },
            ct);

        // Strip markdown code fences if present
        proposedToolsJson = StripCodeFences(proposedToolsJson);

        try
        {
            var toolsDoc = JsonDocument.Parse(proposedToolsJson);
            await sessions.UpdateProposedToolsAsync(sessionId, toolsDoc);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Could not parse proposed tools JSON, storing as-is");
        }

        await sessions.SetPlanPresentedAsync(sessionId);

        // Reload session with updated port/status
        session = (await sessions.GetByIdAsync(sessionId))!;

        // Render plan template
        var planTemplate  = TemplateEngine.ReadTemplate(ScaffoldingRoot, "intake/plan-format.md");
        var department    = ExtractDepartment(session.IntakeAnswers);
        var baseClass     = IsStateless(session.IntakeAnswers) ? "AgentBase" : "BaseAgentService";
        var schemaName    = agentName.ToLower() + "_schema";
        var routingKws    = ExtractRoutingKeywords(session.IntakeAnswers);

        var tokens = new Dictionary<string, string>
        {
            ["AgentName"]         = agentName,
            ["AgentNameLower"]    = agentName.ToLower(),
            ["Port"]              = port.ToString(),
            ["Department"]        = department,
            ["BaseClass"]         = baseClass,
            ["SchemaName"]        = schemaName,
            ["RoutingKeywords"]   = routingKws,
            ["ProposedTools"]     = proposedToolsJson,
            ["IntakeAnswers"]     = session.IntakeAnswers ?? "",
        };

        return TemplateEngine.Render(planTemplate, tokens);
    }

    public async Task<string> GenerateSystemPromptAsync(
        Guid sessionId,
        string agentRole,
        string agentRoleDescription,
        CancellationToken ct = default)
    {
        var session = await sessions.GetByIdAsync(sessionId)
            ?? throw new InvalidOperationException($"Session {sessionId} not found.");

        var agentName  = session.AgentName ?? ExtractAgentName(session.IntakeAnswers ?? "");
        var sysTemplate = TemplateEngine.ReadTemplate(ScaffoldingRoot, "SystemPrompt.tmpl");

        var tokens = new Dictionary<string, string>
        {
            ["AgentName"]              = agentName,
            ["AgentRole"]              = agentRole,
            ["AgentRoleDescription"]   = agentRoleDescription,
            ["KnowledgeStoreDescription"] = "",
        };

        return TemplateEngine.Render(sysTemplate, tokens);
    }

    // ── Intake answer parsing helpers ──────────────────────────────────────────

    private static string ExtractAgentName(string? answers)
    {
        if (string.IsNullOrEmpty(answers)) return "NewAgent";
        // Look for "Agent Name:" or "1." line
        foreach (var line in answers.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Agent Name:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
            {
                var name = trimmed.Split(':', 2).Last().Trim();
                // Pascal-case, strip spaces
                return ToPascalCase(name);
            }
        }
        // Fall back: first non-empty word
        return ToPascalCase(answers.Split(['\n', ' '], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "NewAgent");
    }

    private static string ExtractDepartment(string? answers)
    {
        if (string.IsNullOrEmpty(answers)) return "General";
        foreach (var line in answers.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Department:", StringComparison.OrdinalIgnoreCase))
                return trimmed.Split(':', 2).Last().Trim();
        }
        return "General";
    }

    private static bool IsStateless(string? answers)
    {
        if (string.IsNullOrEmpty(answers)) return false;
        return answers.Contains("stateless", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractRoutingKeywords(string? answers)
    {
        if (string.IsNullOrEmpty(answers)) return "";
        foreach (var line in answers.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Routing Keywords:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Keywords:", StringComparison.OrdinalIgnoreCase))
                return trimmed.Split(':', 2).Last().Trim();
        }
        return "";
    }

    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "NewAgent";
        var parts = input.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p =>
            char.ToUpper(p[0]) + (p.Length > 1 ? p[1..] : "")));
    }

    private static string StripCodeFences(string content)
    {
        var lines = content.Split('\n').ToList();
        if (lines.Count > 0 && lines[0].StartsWith("```"))
            lines.RemoveAt(0);
        if (lines.Count > 0 && lines[^1].TrimEnd().StartsWith("```"))
            lines.RemoveAt(lines.Count - 1);
        return string.Join('\n', lines).Trim();
    }
}
