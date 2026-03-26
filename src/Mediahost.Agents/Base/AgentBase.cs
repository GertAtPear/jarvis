using System.Text.Json;
using Mediahost.Llm.Models;
using Mediahost.Llm.Services;
using Microsoft.Extensions.Logging;

namespace Mediahost.Agents.Base;

/// <summary>
/// Lightweight abstract base for agents that want to build their own HandleMessageAsync
/// but still leverage the standard agentic LLM loop.
///
/// Provides <see cref="RunAgentLoopAsync"/> — the raw tool-use loop without memory management.
/// Agents that need persistent memory and history should extend
/// <see cref="Mediahost.Agents.Services.BaseAgentService"/> instead.
/// </summary>
public abstract class AgentBase(LlmService llm, ILogger logger)
{
    protected abstract string AgentName { get; }
    protected abstract string SystemPrompt { get; }
    protected abstract IReadOnlyList<ToolDefinition> ToolDefinitions { get; }

    /// <summary>
    /// Execute a single named tool and return its JSON result string.
    /// </summary>
    protected abstract Task<string> ExecuteToolAsync(
        string toolName,
        IReadOnlyDictionary<string, JsonElement> parameters,
        CancellationToken ct);

    /// <summary>
    /// Runs the standard agentic loop: sends the message to the LLM, executes tool calls,
    /// feeds results back, and repeats until the model returns a final text response
    /// or <paramref name="maxIterations"/> is reached.
    ///
    /// Does NOT manage sessions, history, or memory — callers are responsible for that.
    /// </summary>
    protected async Task<string> RunAgentLoopAsync(
        string userMessage,
        Guid sessionId,
        IReadOnlyList<LlmContent>? attachments = null,
        int maxIterations = 15,
        CancellationToken ct = default)
    {
        var messages = new List<LlmMessage>
        {
            new("user", attachments?.Count > 0
                ? [new TextContent(userMessage), ..attachments]
                : [new TextContent(userMessage)])
        };

        for (var i = 0; i < maxIterations; i++)
        {
            var request = new LlmRequest(
                SystemPrompt: SystemPrompt,
                Messages: messages,
                Tools: ToolDefinitions
            );

            LlmResponse response;
            try
            {
                response = (await llm.CompleteAsync(AgentName, request, sessionId, ct)).Response;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{Agent}] LLM call failed in RunAgentLoopAsync (iteration {I})",
                    AgentName, i);
                return "I'm temporarily unavailable. Please try again in a moment.";
            }

            // Build assistant turn content
            var assistantContent = new List<LlmContent>();
            if (!string.IsNullOrEmpty(response.TextContent))
                assistantContent.Add(new TextContent(response.TextContent));
            foreach (var tu in response.ToolUses)
                assistantContent.Add(tu);

            if (assistantContent.Count > 0)
                messages.Add(new LlmMessage("assistant", assistantContent));

            if (response.StopReason != StopReason.ToolUse || response.ToolUses.Count == 0)
                return response.TextContent ?? "[Agent returned no text content]";

            // Execute all tool calls and feed results back
            var toolResults = new List<LlmContent>();
            foreach (var toolUse in response.ToolUses)
            {
                logger.LogDebug("[{Agent}] Tool call: {Tool} (session {Session})",
                    AgentName, toolUse.Name, sessionId);

                string toolResult;
                try
                {
                    var parameters = toolUse.Input.RootElement
                        .EnumerateObject()
                        .ToDictionary(p => p.Name, p => p.Value);
                    toolResult = await ExecuteToolAsync(toolUse.Name, parameters, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[{Agent}] Tool {Tool} threw unexpectedly", AgentName, toolUse.Name);
                    toolResult = JsonSerializer.Serialize(new
                    {
                        error = true,
                        message = $"Tool execution failed: {ex.Message}"
                    });
                }

                toolResults.Add(new ToolResultContent(toolUse.Id, toolResult, ToolName: toolUse.Name));
            }

            messages.Add(new LlmMessage("tool_result", toolResults));
        }

        logger.LogWarning("[{Agent}] Hit maxIterations ({Max}) without completing", AgentName, maxIterations);
        return "[Agent reached maximum tool iterations without completing the task. " +
               "Try breaking the request into smaller steps.]";
    }
}
