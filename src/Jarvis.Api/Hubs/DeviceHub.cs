using Jarvis.Api.Data;
using Jarvis.Api.Services;
using Microsoft.AspNetCore.SignalR;

namespace Jarvis.Api.Hubs;

/// <summary>
/// SignalR hub for Local Agent Host (LAH) devices.
///
/// Connection flow:
/// 1. LAH connects to /hubs/device via persistent WebSocket
/// 2. LAH calls RegisterDevice(deviceToken, moduleListJson)
/// 3. Hub validates token against the devices table, marks device online
/// 4. When an agent calls a laptop tool, DeviceToolForwarder sends ExecuteTool to this hub
/// 5. LAH executes the tool and calls ToolResult with the outcome
/// 6. DeviceToolForwarder resolves the waiting TaskCompletionSource
///
/// Hub is transient — state lives in singleton DeviceConnectionTracker + DeviceToolForwarder.
/// </summary>
public class DeviceHub(
    DeviceRepository deviceRepo,
    DeviceConnectionTracker tracker,
    IDeviceToolForwarder forwarder,
    ILogger<DeviceHub> logger) : Hub
{
    /// <summary>
    /// Called by the LAH immediately after connecting.
    /// Authenticates the device token and marks the device as online.
    /// </summary>
    public async Task RegisterDevice(string deviceToken, string moduleListJson)
    {
        try
        {
            var device = await deviceRepo.GetByTokenAsync(deviceToken);

            if (device is null)
            {
                logger.LogWarning("[DeviceHub] RegisterDevice failed: unknown device token from connection {ConnId}",
                    Context.ConnectionId);
                await Clients.Caller.SendAsync("RegistrationFailed", "Invalid device token");
                Context.Abort();
                return;
            }

            tracker.Register(device.Id.ToString(), Context.ConnectionId);

            var ipAddress = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString();
            await deviceRepo.UpdateStatusAsync(
                device.Id, "online", DateTimeOffset.UtcNow,
                null, ipAddress, null, moduleListJson);

            logger.LogInformation("[DeviceHub] Device '{Name}' ({Id}) connected from {Ip}",
                device.Name, device.Id, ipAddress ?? "unknown");

            await Clients.Caller.SendAsync("RegistrationSuccess", device.Id.ToString(), device.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[DeviceHub] RegisterDevice threw for connection {ConnId}", Context.ConnectionId);
            Context.Abort();
        }
    }

    /// <summary>
    /// Called by the LAH with the result of a tool execution.
    /// Resolves the waiting TaskCompletionSource in DeviceToolForwarder.
    /// </summary>
    public Task ToolResult(string correlationId, string result, bool success, string? errorMessage)
    {
        forwarder.CompleteToolCall(correlationId, result, success, errorMessage);
        return Task.CompletedTask;
    }

    // ── Hub lifecycle ─────────────────────────────────────────────────────────

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var deviceId = tracker.GetDeviceId(Context.ConnectionId);
        tracker.Unregister(Context.ConnectionId);

        if (deviceId is not null && Guid.TryParse(deviceId, out var id))
        {
            await deviceRepo.UpdateStatusAsync(
                id, "offline", DateTimeOffset.UtcNow,
                null, null, null, null);

            logger.LogInformation("[DeviceHub] Device {DeviceId} disconnected", deviceId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
