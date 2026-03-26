using System.Text.Json;

namespace Mediahost.Llm.Models;

public abstract record LlmContent(string Type);

public record TextContent(string Text)
    : LlmContent("text");

public record ImageContent(string Base64Data, string MimeType)
    : LlmContent("image");

public record DocumentContent(string Base64Data, string MimeType, string? Title)
    : LlmContent("document");

public record ToolUseContent(string Id, string Name, JsonDocument Input)
    : LlmContent("tool_use");

public record ToolResultContent(string ToolUseId, string Result, bool IsError = false, string ToolName = "")
    : LlmContent("tool_result");
