using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LaptopHost.Services;

/// <summary>
/// Maintains a persistent SignalR connection to Jarvis's DeviceHub.
/// Reconnects automatically with exponential backoff.
/// Listens for ExecuteTool messages and dispatches to ToolDispatchService.
/// </summary>
public class JarvisConnectionService(
    LahConfig config,
    ToolDispatchService dispatch,
    ILogger<JarvisConnectionService> logger) : IHostedService, IAsyncDisposable
{
    private static readonly TimeSpan[] BackoffDelays =
        [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
         TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];

    private HubConnection? _connection;
    private CancellationTokenSource? _cts;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!config.IsRegistered)
        {
            logger.LogError("[LAH] Device is not registered. Run with --register <token> first.");
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => RunConnectionLoopAsync(_cts.Token), _cts.Token);
        await Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_connection is not null)
        {
            await _connection.StopAsync(cancellationToken);
            await _connection.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_connection is not null)
            await _connection.DisposeAsync();
        _cts?.Dispose();
    }

    // ── Connection loop ───────────────────────────────────────────────────────

    private async Task RunConnectionLoopAsync(CancellationToken ct)
    {
        var attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAsync(ct);
                attempt = 0; // reset backoff on successful connect

                // Wait until connection drops
                await WaitForDisconnectAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[LAH] Connection failed (attempt {Attempt})", attempt + 1);
            }

            if (ct.IsCancellationRequested) break;

            var delay = BackoffDelays[Math.Min(attempt, BackoffDelays.Length - 1)];
            logger.LogInformation("[LAH] Reconnecting in {Delay}...", delay);
            await Task.Delay(delay, ct);
            attempt++;
        }
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        var hubUrl = $"{config.JarvisBaseUrl.TrimEnd('/')}/hubs/device";

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(BackoffDelays)
            .Build();

        // ── Hub event handlers ────────────────────────────────────────────────

        _connection.On<string, string>("RegistrationSuccess", (deviceId, deviceName) =>
        {
            logger.LogInformation("[LAH] Registered as '{DeviceName}' ({DeviceId}) at {Url}",
                deviceName, deviceId, hubUrl);
        });

        _connection.On<string>("RegistrationFailed", reason =>
        {
            logger.LogError("[LAH] Registration rejected by Jarvis: {Reason}", reason);
        });

        // ExecuteTool: Jarvis forwards a tool call from an agent
        _connection.On<string, string, string, string, bool>(
            "ExecuteTool",
            async (deviceId, correlationId, toolName, parametersJson, requireConfirm) =>
            {
                logger.LogInformation("[LAH] Tool request: '{Tool}' (correlation: {CorrelationId})", toolName, correlationId);

                var (result, success) = await dispatch.ExecuteAsync(
                    toolName, parametersJson, requireConfirm, ct);

                try
                {
                    await _connection.InvokeAsync("ToolResult", correlationId, result, success,
                        success ? null : "Tool execution failed", ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[LAH] Failed to send ToolResult for correlation {Id}", correlationId);
                }
            });

        _connection.Reconnecting += ex =>
        {
            logger.LogWarning("[LAH] Connection lost, reconnecting... ({Reason})", ex?.Message);
            return Task.CompletedTask;
        };

        _connection.Reconnected += connectionId =>
        {
            logger.LogInformation("[LAH] Reconnected (connectionId: {Id})", connectionId);
            return RegisterAsync(ct);
        };

        _connection.Closed += ex =>
        {
            logger.LogWarning("[LAH] Connection closed: {Reason}", ex?.Message ?? "graceful");
            return Task.CompletedTask;
        };

        // ── Connect and register ──────────────────────────────────────────────

        await _connection.StartAsync(ct);
        logger.LogInformation("[LAH] Connected to {Url}", hubUrl);
        await RegisterAsync(ct);
    }

    private async Task RegisterAsync(CancellationToken ct)
    {
        if (_connection is null) return;
        var moduleListJson = dispatch.GetModuleListJson();
        await _connection.InvokeAsync("RegisterDevice", config.DeviceToken, moduleListJson, ct);
    }

    private async Task WaitForDisconnectAsync(CancellationToken ct)
    {
        if (_connection is null) return;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => tcs.TrySetResult(true));

        _connection.Closed += _ =>
        {
            tcs.TrySetResult(false);
            return Task.CompletedTask;
        };

        await tcs.Task;
    }
}
