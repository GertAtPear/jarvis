using System.Text.Json;
using LaptopHost.Modules;
using Microsoft.Extensions.Logging;

namespace LaptopHost.Services;

/// <summary>
/// Routes tool call requests from Jarvis to the correct ILaptopToolModule.
/// Enforces confirmation for tools marked RequireConfirm.
/// </summary>
public class ToolDispatchService(
    IEnumerable<ILaptopToolModule> modules,
    ConfirmationService confirmation,
    ILogger<ToolDispatchService> logger)
{
    private readonly Dictionary<string, (ILaptopToolModule Module, LaptopToolSpec Spec)> _toolMap =
        modules.SelectMany(m => m.GetDefinitions().Select(d => (m, d)))
               .ToDictionary(x => x.d.Name, x => (x.m, x.d));

    /// <summary>Returns all tool definitions for advertising to Jarvis on connect.</summary>
    public IReadOnlyList<LaptopToolSpec> AllDefinitions =>
        _toolMap.Values.Select(v => v.Spec).ToList();

    /// <summary>Advertised module list as JSON (sent to Jarvis on SignalR connect).</summary>
    public string GetModuleListJson()
    {
        var modules = _toolMap.Values
            .GroupBy(v => v.Module.ModuleName)
            .Select(g => new
            {
                module = g.Key,
                tools  = g.Select(t => t.Spec.Name).ToList()
            }).ToList();

        return JsonSerializer.Serialize(modules);
    }

    /// <summary>
    /// Execute a tool call. Returns a JSON result string.
    /// Never throws — all errors are returned as {"error": "..."}.
    /// </summary>
    public async Task<(string result, bool success)> ExecuteAsync(
        string toolName,
        string parametersJson,
        bool requireConfirm,
        CancellationToken ct = default)
    {
        if (!_toolMap.TryGetValue(toolName, out var entry))
        {
            var msg = $"Unknown tool: '{toolName}'. Available tools: {string.Join(", ", _toolMap.Keys)}";
            logger.LogWarning("[Dispatch] {Message}", msg);
            return (JsonSerializer.Serialize(new { error = msg }), false);
        }

        var (module, spec) = entry;

        // Confirm if required (either by Jarvis permission config or module default)
        if (requireConfirm || spec.RequireConfirm)
        {
            JsonDocument parsedParams;
            try
            {
                parsedParams = JsonDocument.Parse(parametersJson);
            }
            catch
            {
                parsedParams = JsonDocument.Parse("{}");
            }

            var approved = await confirmation.RequestAsync(toolName, parsedParams, ct);
            if (!approved)
            {
                logger.LogWarning("[Dispatch] Tool '{Tool}' denied by user", toolName);
                return (JsonSerializer.Serialize(new { error = "User denied this action" }), false);
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(parametersJson);
            logger.LogInformation("[Dispatch] Executing tool '{Tool}' via module '{Module}'",
                toolName, module.ModuleName);

            var result = await module.ExecuteAsync(toolName, doc, ct);
            return (result, true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Dispatch] Tool '{Tool}' threw", toolName);
            return (JsonSerializer.Serialize(new { error = ex.Message }), false);
        }
    }
}
