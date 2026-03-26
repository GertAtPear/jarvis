namespace Mediahost.Agents.Services;

public interface IAgentService
{
    Task<AgentResponse> HandleMessageAsync(string message, Guid? sessionId, CancellationToken ct = default);
}

public record AgentResponse(string Response, Guid SessionId, int ToolCallCount);
