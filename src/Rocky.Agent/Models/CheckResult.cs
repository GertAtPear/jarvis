namespace Rocky.Agent.Models;

/// <summary>
/// Result of a single check run for a watched service.
/// </summary>
public class CheckResult
{
    public Guid     Id              { get; set; }
    public Guid     ServiceId       { get; set; }
    public bool     IsHealthy       { get; set; }
    public string?  Detail          { get; set; }
    public long     DurationMs      { get; set; }
    public DateTime CheckedAt       { get; set; }
}
