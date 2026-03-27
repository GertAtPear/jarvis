namespace Sam.Agent.Models;

public record DatabaseRecord
{
    public Guid             Id              { get; init; }
    public string           Name            { get; init; } = "";
    public string           DisplayName     { get; init; } = "";
    public string           DbType          { get; init; } = "";
    public string           Host            { get; init; } = "";
    public int              Port            { get; init; }
    public string           DbName          { get; init; } = "";
    public string?          VaultSecretPath { get; init; }
    public string           Status          { get; init; } = "";
    public DateTimeOffset?  LastScannedAt   { get; init; }
    public string?          Notes           { get; init; }
    public DateTimeOffset   CreatedAt       { get; init; }
}

public record SlowQueryRecord
{
    public Guid            Id             { get; init; }
    public Guid            DatabaseId     { get; init; }
    public string          QueryHash      { get; init; } = "";
    public string          QueryText      { get; init; } = "";
    public double          AvgDurationMs  { get; init; }
    public double          MaxDurationMs  { get; init; }
    public int             ExecutionCount { get; init; }
    public string?         SchemaName     { get; init; }
    public DateTimeOffset  CapturedAt     { get; init; }
}

public record ConnectionStatsRecord
{
    public Guid            Id                 { get; init; }
    public Guid            DatabaseId         { get; init; }
    public int             ActiveConnections  { get; init; }
    public int             MaxConnections     { get; init; }
    public int             IdleConnections    { get; init; }
    public int             WaitingQueries     { get; init; }
    public DateTimeOffset  CapturedAt         { get; init; }
}

public record TableStatsRecord
{
    public Guid            Id            { get; init; }
    public Guid            DatabaseId    { get; init; }
    public string          SchemaName    { get; init; } = "";
    public string          TableName     { get; init; } = "";
    public long            RowEstimate   { get; init; }
    public long            DataSizeBytes { get; init; }
    public long            IndexSizeBytes { get; init; }
    public DateTimeOffset  CapturedAt    { get; init; }
}

public record ReplicationStatusRecord
{
    public Guid            Id                    { get; init; }
    public Guid            DatabaseId            { get; init; }
    public string          Role                  { get; init; } = "";
    public double?         ReplicationLagSeconds { get; init; }
    public bool            IsConnected           { get; init; }
    public string?         ReplicaHost           { get; init; }
    public DateTimeOffset  CapturedAt            { get; init; }
}

public record DiscoveryLogRecord
{
    public Guid            Id          { get; init; }
    public Guid?           DatabaseId  { get; init; }
    public string          ScanType    { get; init; } = "";
    public string          Status      { get; init; } = "";
    public string?         DetailsJson { get; init; }
    public int             DurationMs  { get; init; }
    public DateTimeOffset  RanAt       { get; init; }
}
