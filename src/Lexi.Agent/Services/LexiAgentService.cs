using Lexi.Agent.Data;
using Lexi.Agent.SystemPrompts;
using Lexi.Agent.Tools;
using Mediahost.Agents.Data;
using Mediahost.Agents.Services;
using Mediahost.Llm.Services;

namespace Lexi.Agent.Services;

public class LexiAgentService(
    LlmService llm,
    LexiToolExecutor executor,
    LexiMemoryService memory,
    SharedMemoryService sharedMemory,
    ILogger<LexiAgentService> logger)
    : BaseAgentService(llm, executor, memory, sharedMemory, logger)
{
    protected override string AgentName => "lexi";
    protected override string BaseSystemPrompt => LexiSystemPrompt.Prompt;
    protected override int MaxTokens => 8192;
}
