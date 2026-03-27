namespace Nadia.Agent.Models;

public record NetworkInterfaceRecord
{
    public Guid            Id              { get; init; }
    public string          Name            { get; init; } = "";
    public string          DisplayName     { get; init; } = "";
    public string          IfType          { get; init; } = "";
    public string?         IpAddress       { get; init; }
    public string?         Subnet          { get; init; }
    public bool            IsActive        { get; init; }
    public string?         VaultSecretPath { get; init; }
    public DateTimeOffset  CreatedAt       { get; init; }
}

public record LatencyHistoryRecord
{
    public Guid            Id            { get; init; }
    public Guid            InterfaceId   { get; init; }
    public string          TargetHost    { get; init; } = "";
    public double          RttMs         { get; init; }
    public double          PacketLossPct { get; init; }
    public DateTimeOffset  ProbedAt      { get; init; }
}

public record WifiNodeRecord
{
    public Guid            Id               { get; init; }
    public string          Ssid             { get; init; } = "";
    public string          Bssid            { get; init; } = "";
    public int?            Channel          { get; init; }
    public int?            SignalDbm        { get; init; }
    public int?            ConnectedClients { get; init; }
    public string?         ApHost           { get; init; }
    public DateTimeOffset  ScannedAt        { get; init; }
}

public record DnsCheckRecord
{
    public Guid            Id            { get; init; }
    public string          ResolverHost  { get; init; } = "";
    public string          RecordType    { get; init; } = "";
    public string          QueryName     { get; init; } = "";
    public string?         ResolvedValue { get; init; }
    public int             ResolutionMs  { get; init; }
    public bool            IsHealthy     { get; init; }
    public DateTimeOffset  CheckedAt     { get; init; }
}

public record FailoverEventRecord
{
    public Guid             Id              { get; init; }
    public string?          FromInterface   { get; init; }
    public string?          ToInterface     { get; init; }
    public string?          TriggerReason   { get; init; }
    public DateTimeOffset   DetectedAt      { get; init; }
    public DateTimeOffset?  ResolvedAt      { get; init; }
    public int?             DurationSeconds { get; init; }
}
