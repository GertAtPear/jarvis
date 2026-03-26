using System.Text.Json;

namespace Andrew.Agent.Models;

public class NetworkFact
{
    public Guid Id { get; set; }
    public string FactKey { get; set; } = default!;
    public JsonDocument FactValue { get; set; } = default!;
    public DateTime CheckedAt { get; set; }
}
