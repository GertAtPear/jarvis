using Andrew.Agent.Data;
using Andrew.Agent.SystemPrompts;
using Andrew.Agent.Tools;
using Mediahost.Agents.Data;
using Mediahost.Agents.Services;
using Mediahost.Llm.Services;

namespace Andrew.Agent.Services;

public sealed class AndrewAgentService : BaseAgentService
{
    public AndrewAgentService(
        LlmService llm,
        AndrewToolExecutor executor,
        AndrewMemoryService memory,
        ILogger<AndrewAgentService> logger)
        : base(llm, executor, memory, logger) { }

    protected override string AgentName => "andrew";
    protected override string BaseSystemPrompt => AndrewSystemPrompt.Prompt;
    protected override int MaxTokens => 8192;
}
