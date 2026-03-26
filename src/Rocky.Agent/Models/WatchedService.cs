namespace Rocky.Agent.Models;

/// <summary>
/// A pipeline service that Rocky monitors on a schedule.
/// </summary>
public class WatchedService
{
    public Guid   Id              { get; set; }
    public string Name            { get; set; } = string.Empty;
    public string DisplayName     { get; set; } = string.Empty;
    public string CheckType       { get; set; } = string.Empty;   // http_health|tcp_port|container_running|ssh_process|sql_select|kafka_lag|radio_capture
    public string CheckConfig     { get; set; } = "{}";           // JSON config per check type
    public int    IntervalSeconds { get; set; } = 60;
    public bool   Enabled         { get; set; } = true;
    public string? VaultSecretPath { get; set; }
    public string? ServerId       { get; set; }                    // FK → andrew_schema.servers (optional)
    public DateTime CreatedAt     { get; set; }
    public DateTime UpdatedAt     { get; set; }
}
