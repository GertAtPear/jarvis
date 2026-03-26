using Mediahost.Llm.Models;

namespace Mediahost.Agents.Data;

public interface IAgentMemoryService
{
    Task EnsureSessionAsync(Guid sessionId, CancellationToken ct = default);
    Task<List<LlmMessage>> LoadHistoryAsync(Guid sessionId, CancellationToken ct = default);
    Task SaveTurnAsync(Guid sessionId, string userMsg, string assistantMsg, CancellationToken ct = default);
    Task<Dictionary<string, string>> LoadFactsAsync(CancellationToken ct = default);
    Task RememberFactAsync(string key, string value, CancellationToken ct = default);
    Task ForgetFactAsync(string key, CancellationToken ct = default);
}
