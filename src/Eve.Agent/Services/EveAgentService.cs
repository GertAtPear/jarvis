using Eve.Agent.Data;
using Eve.Agent.SystemPrompts;
using Eve.Agent.Tools;
using Mediahost.Agents.Services;
using Mediahost.Llm.Services;

namespace Eve.Agent.Services;

public sealed class EveAgentService : BaseAgentService
{
    private readonly EveMemoryService _eveMemory;

    public EveAgentService(
        LlmService llm,
        EveToolExecutor executor,
        EveMemoryService memory,
        ILogger<EveAgentService> logger)
        : base(llm, executor, memory, logger)
    {
        _eveMemory = memory;
    }

    protected override string AgentName => "eve";
    protected override string BaseSystemPrompt => EveSystemPrompt.Prompt;

    protected override Task<string?> LoadAdditionalContextAsync(Guid sessionId, CancellationToken ct) =>
        _eveMemory.LoadDailyContextAsync(ct);
}
