using Mediahost.Agents.Data;
using Mediahost.Agents.Services;
using Mediahost.Llm.Services;
using Sam.Agent.Data;
using Sam.Agent.SystemPrompts;
using Sam.Agent.Tools;

namespace Sam.Agent.Services;

public sealed class SamAgentService : BaseAgentService
{
    public SamAgentService(
        LlmService llm,
        SamToolExecutor executor,
        SamMemoryService memory,
        SharedMemoryService sharedMemory,
        ILogger<SamAgentService> logger)
        : base(llm, executor, memory, sharedMemory, logger) { }

    protected override string AgentName => "sam";
    protected override string BaseSystemPrompt => SamSystemPrompt.Prompt;
    protected override int MaxTokens => 8192;
}
