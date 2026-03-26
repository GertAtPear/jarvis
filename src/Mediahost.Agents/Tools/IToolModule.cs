using System.Text.Json;
using Mediahost.Llm.Models;

namespace Mediahost.Agents.Tools;

public interface IToolModule
{
    IEnumerable<ToolDefinition> GetDefinitions();

    /// <summary>
    /// Executes the named tool. Returns null if this module does not handle the tool name.
    /// </summary>
    Task<string?> TryExecuteAsync(string toolName, JsonDocument input, CancellationToken ct = default);
}
