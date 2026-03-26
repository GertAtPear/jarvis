using Mediahost.Llm.Models;
using Mediahost.Llm.Services;
using Rex.Agent.SystemPrompts;

namespace Rex.Agent.Services;

/// <summary>
/// Spawns a focused LLM session (using "rex-dev" routing) to act as a
/// temporary developer agent. Used by Rex to write or review code files
/// without polluting Rex's own conversation history.
/// </summary>
public class DeveloperAgentService(LlmService llm, ILogger<DeveloperAgentService> logger)
{
    /// <summary>
    /// Asks the temp dev agent to write a single complete file.
    /// Returns raw file content (no markdown fences, no explanation).
    /// </summary>
    public async Task<string> DevelopFileAsync(
        string task,
        string targetFile,
        Dictionary<string, string> contextFiles,
        CancellationToken ct = default)
    {
        var userMessage = BuildDevelopPrompt(task, targetFile, contextFiles);
        return await CompleteAsync(userMessage, ct);
    }

    /// <summary>
    /// Asks the temp dev agent to produce a structured markdown implementation plan.
    /// </summary>
    public async Task<string> PlanTaskAsync(
        string task,
        Dictionary<string, string> contextFiles,
        CancellationToken ct = default)
    {
        var contextBlock = BuildContextBlock(contextFiles);
        var userMessage = $"""
            Produce a structured markdown implementation plan for the following task.
            Include: files to create or modify, key changes per file, any new dependencies.

            TASK:
            {task}

            {contextBlock}
            """;
        return await CompleteAsync(userMessage, ct);
    }

    /// <summary>
    /// Asks the temp dev agent to review a git diff and return a correctness summary.
    /// </summary>
    public async Task<string> ReviewChangesAsync(string diff, string taskDescription, CancellationToken ct = default)
    {
        var userMessage = $"""
            Review the following git diff for the task described below.
            Report: (1) correctness — does it fully implement the task? (2) any bugs or missed edge cases.
            Be concise.

            TASK:
            {taskDescription}

            DIFF:
            {diff}
            """;
        return await CompleteAsync(userMessage, ct);
    }

    // ── Internal ───────────────────────────────────────────────────────────────

    private async Task<string> CompleteAsync(string userMessage, CancellationToken ct)
    {
        var request = new LlmRequest(
            SystemPrompt: DeveloperAgentPrompt.Prompt,
            Messages: [new LlmMessage("user", [new TextContent(userMessage)])],
            Tools: [],
            MaxTokens: 8192
        );

        try
        {
            var result = await llm.CompleteAsync("rex-dev", request, Guid.NewGuid(), ct);
            return result.Response.TextContent ?? "";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Developer agent LLM call failed");
            throw;
        }
    }

    private static string BuildDevelopPrompt(string task, string targetFile, Dictionary<string, string> contextFiles)
    {
        var contextBlock = BuildContextBlock(contextFiles);
        return $"""
            Write the complete content for: {targetFile}

            TASK:
            {task}

            {contextBlock}
            """;
    }

    private static string BuildContextBlock(Dictionary<string, string> contextFiles)
    {
        if (contextFiles.Count == 0) return "";

        var parts = contextFiles.Select(kv =>
            $"FILE: {kv.Key}\n```\n{kv.Value}\n```");
        return "CONTEXT FILES:\n" + string.Join("\n\n", parts);
    }
}
