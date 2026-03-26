using System.Text.Json;

namespace Andrew.Agent.Models;

public class ServerInfo
{
    public Guid Id { get; set; }
    public string Hostname { get; set; } = default!;
    public string IpAddress { get; set; } = default!;
    public int SshPort { get; set; } = 22;
    public string? OsName { get; set; }
    public string? OsVersion { get; set; }
    public int? CpuCores { get; set; }
    public decimal? RamGb { get; set; }
    public decimal? DiskTotalGb { get; set; }
    public decimal? DiskUsedGb { get; set; }
    public string Status { get; set; } = "unknown";
    public string? VaultSecretPath { get; set; }
    public DateTime? LastScannedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public string? Notes { get; set; }
    public JsonDocument? Tags { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
