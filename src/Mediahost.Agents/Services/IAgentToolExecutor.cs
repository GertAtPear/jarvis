using System.Text.Json;
using Mediahost.Llm.Models;

namespace Mediahost.Agents.Services;

public interface IAgentToolExecutor
{
    IReadOnlyList<ToolDefinition> GetTools();
    Task<string> ExecuteAsync(string toolName, JsonDocument input, CancellationToken ct = default);
}
