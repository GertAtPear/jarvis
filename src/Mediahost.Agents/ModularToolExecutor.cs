using System.Text.Json;
using Mediahost.Agents.Services;
using Mediahost.Agents.Tools;
using Mediahost.Llm.Models;

namespace Mediahost.Agents;

/// <summary>
/// Abstract base class that implements IAgentToolExecutor by composing registered IToolModule instances.
/// Subclasses handle their own agent-specific tools by implementing the two abstract members.
/// </summary>
public abstract class ModularToolExecutor(IEnumerable<IToolModule> modules) : IAgentToolExecutor
{
    public IReadOnlyList<ToolDefinition> GetTools() =>
        modules.SelectMany(m => m.GetDefinitions())
               .Concat(GetAgentSpecificDefinitions())
               .ToList();

    public async Task<string> ExecuteAsync(string toolName, JsonDocument input, CancellationToken ct = default)
    {
        foreach (var module in modules)
        {
            var result = await module.TryExecuteAsync(toolName, input, ct);
            if (result is not null)
                return result;
        }
        return await HandleAgentSpecificAsync(toolName, input, ct);
    }

    /// <summary>Returns tool definitions owned by this agent (not handled by any module).</summary>
    protected abstract IEnumerable<ToolDefinition> GetAgentSpecificDefinitions();

    /// <summary>Executes a tool not handled by any registered module.</summary>
    protected abstract Task<string> HandleAgentSpecificAsync(string toolName, JsonDocument input, CancellationToken ct);
}
