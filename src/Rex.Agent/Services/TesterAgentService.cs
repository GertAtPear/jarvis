using Mediahost.Llm.Models;
using Mediahost.Llm.Services;
using Rex.Agent.SystemPrompts;

namespace Rex.Agent.Services;

/// <summary>
/// Spawns a stateless, ephemeral tester LLM session (using "rex-tester" routing).
/// The tester receives a full test specification and executes it — it has no memory
/// between calls.
///
/// Analogous to DeveloperAgentService but for test execution rather than code writing.
/// </summary>
public class TesterAgentService(LlmService llm, ILogger<TesterAgentService> logger)
{
    /// <summary>
    /// Executes a test specification and returns a JSON pass/fail report.
    /// Phase should be "before", "after", or "standalone".
    /// </summary>
    public async Task<string> RunTestsAsync(
        string appName,
        string phase,
        string testSpecJson,
        string? beforeSnapshotPath = null,
        CancellationToken ct = default)
    {
        var userMessage = BuildTestPrompt(appName, phase, testSpecJson, beforeSnapshotPath);
        return await CompleteAsync(userMessage, ct);
    }

    // ── Internal ───────────────────────────────────────────────────────────────

    private async Task<string> CompleteAsync(string userMessage, CancellationToken ct)
    {
        var request = new LlmRequest(
            SystemPrompt: TesterAgentPrompt.Prompt,
            Messages: [new LlmMessage("user", [new TextContent(userMessage)])],
            Tools: [],
            MaxTokens: 4096
        );

        try
        {
            var result = await llm.CompleteAsync("rex-tester", request, Guid.NewGuid(), ct);
            return result.Response.TextContent ?? "{}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tester agent LLM call failed");
            throw;
        }
    }

    private static string BuildTestPrompt(
        string appName,
        string phase,
        string testSpecJson,
        string? beforeSnapshotPath)
    {
        var snapshotNote = beforeSnapshotPath is not null
            ? $"\nBEFORE-SNAPSHOT FILE: {beforeSnapshotPath} — load this from workspace and diff against current state for snapshot tests."
            : "";

        return $"""
            Execute the following test specification for application '{appName}'.
            Phase: {phase.ToUpperInvariant()}
            {snapshotNote}

            TEST SPECIFICATION:
            {testSpecJson}

            Execute all tests now and return the JSON report.
            """;
    }
}
