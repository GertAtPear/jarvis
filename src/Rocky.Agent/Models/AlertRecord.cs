namespace Rocky.Agent.Models;

/// <summary>
/// Alert raised when a service transitions to unhealthy.
/// </summary>
public class AlertRecord
{
    public Guid     Id          { get; set; }
    public Guid     ServiceId   { get; set; }
    public string   Severity    { get; set; } = "warning";   // info|warning|critical
    public string   Message     { get; set; } = string.Empty;
    public bool     Resolved    { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime CreatedAt   { get; set; }
}
