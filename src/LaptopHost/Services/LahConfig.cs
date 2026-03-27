namespace LaptopHost.Services;

/// <summary>
/// Configuration for the Local Agent Host, loaded from laptophost.json
/// and environment variables.
/// </summary>
public class LahConfig
{
    public string JarvisBaseUrl { get; init; } = "http://localhost:5000";
    public string DeviceId      { get; init; } = "";
    public string DeviceToken   { get; init; } = "";
    public string DeviceName    { get; init; } = Environment.MachineName;

    /// <summary>
    /// Tools that require user confirmation before executing.
    /// Each entry is a tool name. If empty, defaults are used from module definitions.
    /// </summary>
    public HashSet<string> RequireConfirmTools { get; init; } = [];

    /// <summary>Auto-approval timeout for confirm-required tools (seconds).</summary>
    public int ConfirmTimeoutSeconds { get; init; } = 30;

    public bool IsRegistered => !string.IsNullOrWhiteSpace(DeviceToken);
}
