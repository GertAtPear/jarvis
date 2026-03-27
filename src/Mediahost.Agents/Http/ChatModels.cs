namespace Mediahost.Agents.Http;

public record ChatRequest(string Message, Guid? SessionId);
public record ChatResponse(string Response, Guid SessionId, int ToolCallCount, string? EscalatedFrom = null);
