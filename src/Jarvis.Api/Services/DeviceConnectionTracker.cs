using System.Collections.Concurrent;

namespace Jarvis.Api.Services;

/// <summary>
/// Singleton that maps device IDs to active SignalR connection IDs.
/// The hub is transient so this singleton holds the state between requests.
/// </summary>
public class DeviceConnectionTracker
{
    // deviceId → connectionId
    private readonly ConcurrentDictionary<string, string> _deviceToConnection = new();
    // connectionId → deviceId (reverse lookup for disconnect)
    private readonly ConcurrentDictionary<string, string> _connectionToDevice = new();

    public void Register(string deviceId, string connectionId)
    {
        // Remove any previous connection for this device
        if (_deviceToConnection.TryGetValue(deviceId, out var oldConn))
            _connectionToDevice.TryRemove(oldConn, out _);

        _deviceToConnection[deviceId]    = connectionId;
        _connectionToDevice[connectionId] = deviceId;
    }

    public void Unregister(string connectionId)
    {
        if (_connectionToDevice.TryRemove(connectionId, out var deviceId))
            _deviceToConnection.TryRemove(deviceId, out _);
    }

    public string? GetConnectionId(string deviceId) =>
        _deviceToConnection.TryGetValue(deviceId, out var conn) ? conn : null;

    public string? GetDeviceId(string connectionId) =>
        _connectionToDevice.TryGetValue(connectionId, out var device) ? device : null;

    public bool IsOnline(string deviceId) =>
        _deviceToConnection.ContainsKey(deviceId);

    public IReadOnlyCollection<string> OnlineDeviceIds =>
        _deviceToConnection.Keys.ToList();
}
