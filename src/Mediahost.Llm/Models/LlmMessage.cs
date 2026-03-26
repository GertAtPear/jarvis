namespace Mediahost.Llm.Models;

public record LlmMessage(
    string Role,   // "user" | "assistant" | "tool_result"
    IReadOnlyList<LlmContent> Content
);
