using System.Text.Json;

namespace Jarvis.Api.Models;

public class AgentRecord
{
    public Guid   Id            { get; init; }
    public string Name          { get; init; } = "";
    public string DisplayName   { get; init; } = "";
    public Guid?   DepartmentId   { get; init; }
    public string? Department     { get; init; }
    public string Description   { get; init; } = "";
    public string? SystemPrompt { get; init; }
    public string? BaseUrl      { get; init; }
    public string HealthPath    { get; init; } = "/health";
    public string Status        { get; init; } = "active";
    public string? CapabilitiesJson     { get; init; }
    public string? RoutingKeywordsJson  { get; init; }
    public int    SortOrder     { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public IReadOnlyList<string> RoutingKeywords =>
        RoutingKeywordsJson is null ? [] :
        JsonSerializer.Deserialize<string[]>(RoutingKeywordsJson) ?? [];

    public IReadOnlyList<string> Capabilities =>
        CapabilitiesJson is null ? [] :
        JsonSerializer.Deserialize<string[]>(CapabilitiesJson) ?? [];
}
