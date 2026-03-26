namespace Mediahost.Llm.Models;

public record LlmRequest(
    string SystemPrompt,
    IReadOnlyList<LlmMessage> Messages,
    IReadOnlyList<ToolDefinition>? Tools = null,
    int? MaxTokens = null,
    float? Temperature = null
);
