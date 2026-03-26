using System.Text.Json;

namespace Mediahost.Llm.Models;

public record ToolDefinition(
    string Name,
    string Description,
    JsonDocument InputSchema   // JSON Schema object
);
