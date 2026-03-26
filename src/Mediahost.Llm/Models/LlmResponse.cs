namespace Mediahost.Llm.Models;

public record LlmResponse(
    string? TextContent,
    IReadOnlyList<ToolUseContent> ToolUses,
    StopReason StopReason,
    TokenUsage Usage
);

public record TokenUsage(int InputTokens, int OutputTokens);

public enum StopReason
{
    EndTurn,
    ToolUse,
    MaxTokens,
    StopSequence
}
