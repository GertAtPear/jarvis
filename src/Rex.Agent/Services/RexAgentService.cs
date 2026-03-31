using Mediahost.Agents.Data;
using Mediahost.Agents.Services;
using Mediahost.Llm.Services;
using Rex.Agent.Data;
using Rex.Agent.SystemPrompts;
using Rex.Agent.Tools;

namespace Rex.Agent.Services;

public sealed class RexAgentService : BaseAgentService
{
    public RexAgentService(
        LlmService llm,
        RexToolExecutor executor,
        RexMemoryService memory,
        SharedMemoryService sharedMemory,
        ILogger<RexAgentService> logger)
        : base(llm, executor, memory, sharedMemory, logger) { }

    protected override string AgentName       => "rex";
    protected override string BaseSystemPrompt => RexSystemPrompt.Prompt;
    protected override int    MaxTokens        => 8192;
}
