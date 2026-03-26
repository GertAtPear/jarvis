using System.Text.Json;

namespace Andrew.Agent.Models;

public class ContainerInfo
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public string ContainerId { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Image { get; set; }
    public string? Status { get; set; }
    public JsonDocument? Ports { get; set; }
    public JsonDocument? EnvVars { get; set; }
    public string? ComposeProject { get; set; }
    public string? ComposeService { get; set; }
    public decimal? CpuPercent { get; set; }
    public decimal? MemMb { get; set; }
    public DateTime? CreatedAtContainer { get; set; }
    public DateTime ScannedAt { get; set; }
}
