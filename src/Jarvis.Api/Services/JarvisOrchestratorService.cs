using System.Diagnostics;
using System.Text;
using Jarvis.Api.Data;
using Jarvis.Api.Models;
using Jarvis.Api.SystemPrompts;
using Mediahost.Llm.Models;
using Mediahost.Llm.Services;
using Mediahost.Shared.Services;

namespace Jarvis.Api.Services;

public class JarvisOrchestratorService(
    DynamicRoutingService routing,
    AgentClientFactory agentClients,
    AgentRegistryRepository registry,
    ConversationService conversation,
    MorningBriefingService briefing,
    IVaultService vault,
    LlmService llm,
    ILogger<JarvisOrchestratorService> logger)
{
    private const int MaxAgenticLoopIterations = 10;
    private const int HistoryMessages = 10;

    public async Task<OrchestratorResponse> HandleAsync(
        string message,
        Guid sessionId,
        List<AttachmentDto>? attachmentDtos = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var agentsUsed = new List<string>();
        var isMorningBriefing = false;

        // 1. Fetch Anthropic key from vault
        var apiKey = await vault.GetSecretAsync("/ai/anthropic", "api_key", ct);
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("Anthropic API key not found in vault at /apis/anthropic.");

        // 2. Morning briefing check
        string? briefingText = null;
        try
        {
            briefingText = await briefing.GetBriefingIfNeededAsync(ct);
            isMorningBriefing = briefingText is not null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Morning briefing check failed — continuing without it");
        }

        // 3. Load recent conversation history
        var isFirstMessage = await conversation.IsFirstMessageAsync(sessionId);
        var history = (await conversation.GetHistoryAsync(sessionId, HistoryMessages)).ToList();

        // 4. Save the user's message
        await conversation.SaveMessageAsync(sessionId, new AgentMessage
        {
            SessionId = sessionId,
            Role      = "user",
            Content   = message
        });

        // 4b. Direct mention shortcut: "eve, ..." or "andrew, ..." or "@rex ..."
        //     Bypasses the Jarvis LLM loop entirely — agent speaks for itself.
        var mention = TryExtractDirectMention(message);
        if (mention is not null)
        {
            var mentionedAgent = await registry.GetByNameAsync(mention.Value.AgentName);
            if (mentionedAgent is { Status: "active" })
            {
                var stripped = mention.Value.StrippedMessage;
                logger.LogInformation("Direct mention routing to {Agent}: {Message}", mentionedAgent.Name, stripped);
                agentsUsed.Add(mentionedAgent.Name);

                var agentResult = await agentClients.SendMessageAsync(mentionedAgent, stripped, sessionId, ct);

                await conversation.SaveMessageAsync(sessionId, new AgentMessage
                {
                    SessionId = sessionId,
                    Role      = "assistant",
                    Content   = agentResult,
                    AgentName = mentionedAgent.Name
                });

                if (isFirstMessage && !string.IsNullOrWhiteSpace(message))
                    _ = Task.Run(async () =>
                    {
                        try { await conversation.UpdateSessionTitleAsync(sessionId, message); }
                        catch (Exception ex) { logger.LogWarning(ex, "Failed to update session title"); }
                    }, CancellationToken.None);

                sw.Stop();
                return new OrchestratorResponse(
                    Response:          agentResult,
                    SessionId:         sessionId,
                    AgentsUsed:        agentsUsed,
                    TotalMs:           (int)sw.ElapsedMilliseconds,
                    IsMorningBriefing: false,
                    SecretPurged:      false
                );
            }
        }

        // 5. Build the message content for this turn
        var userContentBlocks = new List<LlmContent>();

        // Include attachment content blocks before the text
        if (attachmentDtos is { Count: > 0 })
        {
            foreach (var att in attachmentDtos)
            {
                if (att.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    userContentBlocks.Add(new ImageContent(att.Base64Data, att.MimeType));
                else
                    userContentBlocks.Add(new DocumentContent(att.Base64Data, att.MimeType, att.FileName));
            }
        }

        userContentBlocks.Add(new TextContent(message));

        // 6. Build full message list (history + current turn)
        var llmMessages = new List<LlmMessage>();
        foreach (var h in history)
        {
            llmMessages.Add(new LlmMessage(
                h.Role == "assistant" ? "assistant" : "user",
                [new TextContent(h.Content)]
            ));
        }
        llmMessages.Add(new LlmMessage("user", userContentBlocks));

        // 7. Get dynamic tool definitions from agent registry
        var tools = await routing.BuildToolsAsync(ct);

        // 8. Agentic loop
        var assistantResponseText = new StringBuilder();
        var secretPurged = false;

        for (int iteration = 0; iteration < MaxAgenticLoopIterations; iteration++)
        {
            var request = new LlmRequest(
                SystemPrompt: JarvisSystemPrompt.Text,
                Messages: llmMessages,
                Tools: tools.Count > 0 ? tools : null
            );

            var serviceResponse = await llm.CompleteAsync("jarvis", request, sessionId, ct);
            var response = serviceResponse.Response;

            if (!string.IsNullOrEmpty(response.TextContent))
                assistantResponseText.Append(response.TextContent);

            // End of turn — no tool calls
            if (response.StopReason != StopReason.ToolUse || response.ToolUses.Count == 0)
                break;

            // Process tool calls
            var toolResultBlocks = new List<LlmContent>();

            // Add assistant message with tool_use blocks to history
            var assistantBlocks = new List<LlmContent>();
            if (!string.IsNullOrEmpty(response.TextContent))
                assistantBlocks.Add(new TextContent(response.TextContent));
            assistantBlocks.AddRange(response.ToolUses);

            llmMessages.Add(new LlmMessage("assistant", assistantBlocks));

            foreach (var toolUse in response.ToolUses)
            {
                var agent = await routing.ResolveAgentFromToolAsync(toolUse.Name, ct);

                if (agent is null)
                {
                    logger.LogWarning("Unknown tool call: {Tool}", toolUse.Name);
                    toolResultBlocks.Add(new ToolResultContent(toolUse.Id,
                        $"Unknown tool '{toolUse.Name}'.", IsError: true, ToolName: toolUse.Name));
                    continue;
                }

                if (!agentsUsed.Contains(agent.Name))
                    agentsUsed.Add(agent.Name);

                var question = DynamicRoutingService.ExtractQuestion(toolUse.Input);
                logger.LogInformation("Routing to agent {Agent}: {Question}", agent.Name, question);

                var agentResult = await agentClients.SendMessageAsync(agent, question, sessionId, ct);
                toolResultBlocks.Add(new ToolResultContent(toolUse.Id, agentResult, ToolName: toolUse.Name));

                // Always purge on store_secret — credentials must never remain in history
                if (toolUse.Name == "store_secret")
                    secretPurged = true;
            }

            // Add tool results back for next iteration
            llmMessages.Add(new LlmMessage("tool_result", toolResultBlocks));
        }

        var finalResponse = assistantResponseText.ToString();

        // 9. Prepend morning briefing if present
        if (isMorningBriefing && briefingText is not null)
        {
            finalResponse = string.IsNullOrWhiteSpace(finalResponse)
                ? briefingText
                : $"{briefingText}\n\n{finalResponse}";
        }

        // 10. Save assistant response
        await conversation.SaveMessageAsync(sessionId, new AgentMessage
        {
            SessionId = sessionId,
            Role      = "assistant",
            Content   = finalResponse,
            AgentName = "jarvis"
        });

        // 11. Purge the exchange if a secret was stored
        if (secretPurged)
        {
            try { await conversation.PurgeLastExchangeAsync(sessionId); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to purge secret exchange for session {SessionId}", sessionId); }
        }

        // 12. Set session title after first message
        if (isFirstMessage && !string.IsNullOrWhiteSpace(message))
        {
            _ = Task.Run(async () =>
            {
                try { await conversation.UpdateSessionTitleAsync(sessionId, message); }
                catch (Exception ex) { logger.LogWarning(ex, "Failed to update session title"); }
            }, CancellationToken.None);
        }

        sw.Stop();

        return new OrchestratorResponse(
            Response: finalResponse,
            SessionId: sessionId,
            AgentsUsed: agentsUsed,
            TotalMs: (int)sw.ElapsedMilliseconds,
            IsMorningBriefing: isMorningBriefing,
            SecretPurged: secretPurged
        );
    }

    private static (string AgentName, string StrippedMessage)? TryExtractDirectMention(string message)
    {
        var knownAgents = new[] { "andrew", "eve", "rex", "browser" };
        var trimmed = message.TrimStart();

        foreach (var agent in knownAgents)
        {
            if (trimmed.StartsWith($"@{agent} ", StringComparison.OrdinalIgnoreCase))
                return (agent, trimmed[(agent.Length + 2)..].TrimStart());

            if (trimmed.Length > agent.Length &&
                (trimmed[agent.Length] == ',' || trimmed[agent.Length] == ':') &&
                trimmed.StartsWith(agent, StringComparison.OrdinalIgnoreCase))
                return (agent, trimmed[(agent.Length + 1)..].TrimStart());
        }

        return null;
    }

    /// <summary>
    /// Sends a message directly to a named agent, bypassing the Jarvis LLM entirely.
    /// The exchange is saved to conversation history under that agent's name.
    /// </summary>
    public async Task<OrchestratorResponse?> DirectAgentAsync(
        string agentName,
        string message,
        Guid sessionId,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var agent = await registry.GetByNameAsync(agentName);
        if (agent is null || agent.Status != "active")
            return null;

        var isFirstMessage = await conversation.IsFirstMessageAsync(sessionId);

        await conversation.SaveMessageAsync(sessionId, new AgentMessage
        {
            SessionId = sessionId,
            Role      = "user",
            Content   = message
        });

        logger.LogInformation("Direct agent call to {Agent}: {Message}", agentName, message);
        var agentResult = await agentClients.SendMessageAsync(agent, message, sessionId, ct);

        await conversation.SaveMessageAsync(sessionId, new AgentMessage
        {
            SessionId = sessionId,
            Role      = "assistant",
            Content   = agentResult,
            AgentName = agentName
        });

        if (isFirstMessage && !string.IsNullOrWhiteSpace(message))
        {
            _ = Task.Run(async () =>
            {
                try { await conversation.UpdateSessionTitleAsync(sessionId, message); }
                catch (Exception ex) { logger.LogWarning(ex, "Failed to update session title"); }
            }, CancellationToken.None);
        }

        sw.Stop();

        return new OrchestratorResponse(
            Response: agentResult,
            SessionId: sessionId,
            AgentsUsed: [agentName],
            TotalMs: (int)sw.ElapsedMilliseconds,
            IsMorningBriefing: false,
            SecretPurged: false
        );
    }
}
