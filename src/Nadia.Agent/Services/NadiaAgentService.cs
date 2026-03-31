using Mediahost.Agents.Data;
using Mediahost.Agents.Services;
using Mediahost.Agents.Tools;
using Mediahost.Llm.Services;
using Nadia.Agent.SystemPrompts;

namespace Nadia.Agent.Services;

public sealed class NadiaAgentService : BaseAgentService
{
    public NadiaAgentService(
        LlmService llm,
        IAgentToolExecutor executor,
        IAgentMemoryService memory,
        SharedMemoryService sharedMemory,
        ILogger<NadiaAgentService> logger)
        : base(llm, executor, memory, sharedMemory, logger) { }

    protected override string AgentName => "nadia";
    protected override string BaseSystemPrompt => NadiaSystemPrompt.Prompt;
    protected override int MaxTokens => 8192;
}
