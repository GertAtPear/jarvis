using System.Collections.Concurrent;
using System.Text.Json;
using Jarvis.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Jarvis.Api.Services;

/// <summary>
/// Forwards tool call requests to a connected LAH device via SignalR and waits
/// for the result. Uses TaskCompletionSource keyed by correlation ID.
/// </summary>
public interface IDeviceToolForwarder
{
    /// <summary>
    /// Forward a tool call to the specified device and wait for the result.
    /// Throws <see cref="TimeoutException"/> if the device does not respond within the timeout.
    /// Throws <see cref="InvalidOperationException"/> if the device is not connected.
    /// </summary>
    Task<string> ForwardToolAsync(
        Guid deviceId,
        string toolName,
        JsonDocument parameters,
        bool requireConfirm,
        TimeSpan timeout,
        CancellationToken ct = default);

    /// <summary>Called by DeviceHub when the device sends back a ToolResult.</summary>
    void CompleteToolCall(string correlationId, string result, bool success, string? errorMessage);
}

public class DeviceToolForwarder(
    IHubContext<DeviceHub> hubContext,
    DeviceConnectionTracker tracker,
    ILogger<DeviceToolForwarder> logger) : IDeviceToolForwarder
{
    private static readonly JsonSerializerOptions Opts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // Pending calls: correlationId → TaskCompletionSource
    private readonly ConcurrentDictionary<string, TaskCompletionSource<(string result, bool success, string? error)>>
        _pending = new();

    public async Task<string> ForwardToolAsync(
        Guid deviceId,
        string toolName,
        JsonDocument parameters,
        bool requireConfirm,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        var connectionId = tracker.GetConnectionId(deviceId.ToString());
        if (connectionId is null)
            throw new InvalidOperationException(
                $"Device '{deviceId}' is not connected. It may be offline.");

        var correlationId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<(string, bool, string?)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _pending[correlationId] = tcs;

        try
        {
            var paramsJson = parameters.RootElement.ToString();

            // Send ExecuteTool to the specific device connection
            await hubContext.Clients.Client(connectionId).SendAsync(
                "ExecuteTool",
                deviceId.ToString(),
                correlationId,
                toolName,
                paramsJson,
                requireConfirm,
                ct);

            logger.LogDebug("Forwarded tool '{Tool}' to device {DeviceId} (correlation: {CorrelationId})",
                toolName, deviceId, correlationId);

            // Wait for result with timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            try
            {
                var (result, success, error) = await tcs.Task.WaitAsync(cts.Token);

                if (!success)
                    return JsonSerializer.Serialize(new { error = error ?? "Tool execution failed on device" });

                return result;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Device '{deviceId}' did not respond within {timeout.TotalSeconds:F0}s for tool '{toolName}'.");
            }
        }
        finally
        {
            _pending.TryRemove(correlationId, out _);
        }
    }

    public void CompleteToolCall(string correlationId, string result, bool success, string? errorMessage)
    {
        if (_pending.TryGetValue(correlationId, out var tcs))
        {
            tcs.TrySetResult((result, success, errorMessage));
            logger.LogDebug("Tool call completed (correlation: {CorrelationId}, success: {Success})",
                correlationId, success);
        }
        else
        {
            logger.LogWarning("Received ToolResult for unknown correlation ID: {CorrelationId}", correlationId);
        }
    }
}
