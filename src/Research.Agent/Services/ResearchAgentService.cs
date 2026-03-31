using Mediahost.Agents.Data;
using Mediahost.Agents.Services;
using Mediahost.Llm.Services;
using Research.Agent.Data;
using Research.Agent.SystemPrompts;
using Research.Agent.Tools;

namespace Research.Agent.Services;

public sealed class ResearchAgentService : BaseAgentService
{
    public ResearchAgentService(
        LlmService llm,
        ResearchToolExecutor executor,
        ResearchMemoryService memory,
        SharedMemoryService sharedMemory,
        ILogger<ResearchAgentService> logger)
        : base(llm, executor, memory, sharedMemory, logger) { }

    protected override string AgentName       => "research";
    protected override string BaseSystemPrompt => ResearchSystemPrompt.Prompt;
    protected override int    MaxTokens        => 8192;
}
