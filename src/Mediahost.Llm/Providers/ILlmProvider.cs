using Mediahost.Llm.Models;

namespace Mediahost.Llm.Providers;

public interface ILlmProvider
{
    string ProviderName { get; }
    bool SupportsVision { get; }
    bool SupportsToolUse { get; }

    Task<LlmResponse> CompleteAsync(
        string model, LlmRequest request, CancellationToken ct = default);
}
