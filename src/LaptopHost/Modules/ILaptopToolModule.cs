using System.Text.Json;

namespace LaptopHost.Modules;

/// <summary>
/// Defines a capability module for the Local Agent Host.
/// Each module advertises its tools and can execute them.
///
/// Intentionally separate from Mediahost.Agents IToolModule to keep
/// LaptopHost free of server-side dependencies.
/// </summary>
public interface ILaptopToolModule
{
    string ModuleName { get; }
    IEnumerable<LaptopToolSpec> GetDefinitions();
    Task<string> ExecuteAsync(string toolName, JsonDocument parameters, CancellationToken ct = default);
}

public record LaptopToolSpec(
    string Name,
    string Description,
    string InputSchemaJson,
    bool RequireConfirm = false);
