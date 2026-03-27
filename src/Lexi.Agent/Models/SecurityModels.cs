namespace Lexi.Agent.Models;

public record TlsCertificateRecord
{
    public Guid             Id            { get; init; }
    public string           Host          { get; init; } = "";
    public int              Port          { get; init; }
    public string?          SubjectCn     { get; init; }
    public string?          Issuer        { get; init; }
    public string?          SanJson       { get; init; }
    public DateTimeOffset?  ValidFrom     { get; init; }
    public DateTimeOffset?  ValidTo       { get; init; }
    public int?             DaysRemaining { get; init; }
    public bool             IsValid       { get; init; }
    public DateTimeOffset   CheckedAt     { get; init; }
}

public record OpenPortRecord
{
    public Guid            Id          { get; init; }
    public Guid?           ServerId    { get; init; }
    public string          Host        { get; init; } = "";
    public int             Port        { get; init; }
    public string          Protocol    { get; init; } = "";
    public string?         ServiceName { get; init; }
    public string          State       { get; init; } = "";
    public bool            IsExpected  { get; init; }
    public DateTimeOffset  ScannedAt   { get; init; }
}

public record AccessAnomalyRecord
{
    public Guid             Id          { get; init; }
    public string?          SourceIp    { get; init; }
    public string?          CountryCode { get; init; }
    public string?          City        { get; init; }
    public string           EventType   { get; init; } = "";
    public string?          Username    { get; init; }
    public string?          TargetHost  { get; init; }
    public int              EventCount  { get; init; }
    public DateTimeOffset   FirstSeen   { get; init; }
    public DateTimeOffset   LastSeen    { get; init; }
    public bool             IsResolved  { get; init; }
    public DateTimeOffset?  ResolvedAt  { get; init; }
    public string?          Notes       { get; init; }
}

public record CveAlertRecord
{
    public Guid             Id               { get; init; }
    public string           CveId            { get; init; } = "";
    public string           Severity         { get; init; } = "";
    public double?          CvssScore        { get; init; }
    public string?          Description      { get; init; }
    public string?          AffectedSoftware { get; init; }
    public string?          AffectedVersion  { get; init; }
    public DateTimeOffset?  PublishedAt      { get; init; }
    public DateTimeOffset   MatchedAt        { get; init; }
    public bool             IsAcknowledged   { get; init; }
}

public record SoftwareInventoryRecord
{
    public Guid            Id             { get; init; }
    public string          Host           { get; init; } = "";
    public string          PackageName    { get; init; } = "";
    public string          Version        { get; init; } = "";
    public string          PackageManager { get; init; } = "";
    public DateTimeOffset  ScannedAt      { get; init; }
}

public record NetworkDeviceRecord
{
    public Guid            Id          { get; init; }
    public string          MacAddress  { get; init; } = "";
    public string?         IpAddress   { get; init; }
    public string?         Hostname    { get; init; }
    public string?         Vendor      { get; init; }
    public DateTimeOffset  FirstSeen   { get; init; }
    public DateTimeOffset  LastSeen    { get; init; }
    public bool            IsKnown     { get; init; }
    public string?         DeviceName  { get; init; }
    public string?         Notes       { get; init; }
    public DateTimeOffset  ScannedAt   { get; init; }
}

public record ScanLogRecord
{
    public Guid            Id            { get; init; }
    public string          ScanType      { get; init; } = "";
    public string          Status        { get; init; } = "";
    public int?            HostsScanned  { get; init; }
    public int?            FindingsCount { get; init; }
    public int             DurationMs    { get; init; }
    public DateTimeOffset  RanAt         { get; init; }
}
