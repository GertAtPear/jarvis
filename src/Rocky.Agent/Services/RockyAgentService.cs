using System.Text.Json;
using Mediahost.Agents.Base;
using Mediahost.Agents.Services;
using Mediahost.Llm.Models;
using Mediahost.Llm.Services;
using Microsoft.Extensions.Logging;
using Rocky.Agent.SystemPrompts;
using Rocky.Agent.Tools;

namespace Rocky.Agent.Services;

/// <summary>
/// Rocky's agent service. Extends AgentBase for the raw tool-use loop (no memory/session storage).
/// Rocky is stateless — every request stands alone.
/// </summary>
public class RockyAgentService(
    LlmService llm,
    RockyToolExecutor toolExecutor,
    ILogger<RockyAgentService> logger)
    : AgentBase(llm, logger), IAgentService
{
    protected override string AgentName    => "rocky";
    protected override string SystemPrompt => RockySystemPrompt.Text;
    protected override IReadOnlyList<ToolDefinition> ToolDefinitions => RockyToolDefinitions.All;

    public async Task<AgentResponse> HandleMessageAsync(
        string message, Guid? sessionId, CancellationToken ct = default)
    {
        var sid      = sessionId ?? Guid.NewGuid();
        var response = await RunAgentLoopAsync(message, sid, ct: ct);
        return new AgentResponse(response, sid, 0);
    }

    protected override async Task<string> ExecuteToolAsync(
        string toolName,
        IReadOnlyDictionary<string, JsonElement> parameters,
        CancellationToken ct)
        => await toolExecutor.ExecuteAsync(toolName, parameters, ct);
}
