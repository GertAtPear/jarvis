namespace Andrew.Agent.Models;

public class ApplicationInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public Guid? ServerId { get; set; }
    public Guid? ContainerId { get; set; }
    public string? AppType { get; set; }
    public string? Framework { get; set; }
    public int? Port { get; set; }
    public string? ConfigPath { get; set; }
    public string? GitRepoUrl { get; set; }
    public string? HealthCheckUrl { get; set; }
    public string? Notes { get; set; }
    public DateTime? LastSeenRunningAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
