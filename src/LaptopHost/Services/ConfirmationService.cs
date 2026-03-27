using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LaptopHost.Services;

/// <summary>
/// Handles confirmation prompts for destructive or sensitive tool calls.
///
/// Phase 2 implementation: logs a prominent warning and auto-approves after
/// a configurable timeout. A future phase can add a proper GUI prompt or
/// a local HTTP endpoint for browser-based confirmation.
/// </summary>
public class ConfirmationService(LahConfig config, ILogger<ConfirmationService> logger)
{
    /// <summary>
    /// Request confirmation for a tool call.
    /// Returns true if approved, false if denied.
    /// </summary>
    public async Task<bool> RequestAsync(string toolName, JsonDocument parameters, CancellationToken ct = default)
    {
        var paramsPreview = parameters.RootElement.ToString();
        if (paramsPreview.Length > 200) paramsPreview = paramsPreview[..200] + "...";

        logger.LogWarning(
            """
            ╔══════════════════════════════════════════════════════════════════╗
            ║  CONFIRM REQUIRED — AUTO-APPROVING IN {Timeout}s                       ║
            ║  Tool:       {Tool,-58} ║
            ║  Parameters: {Params,-58} ║
            ╚══════════════════════════════════════════════════════════════════╝
            """,
            config.ConfirmTimeoutSeconds, toolName, paramsPreview);

        // Phase 2: auto-approve after timeout
        // Future: await a TaskCompletionSource that can be resolved by a GUI prompt
        await Task.Delay(TimeSpan.FromSeconds(config.ConfirmTimeoutSeconds), ct);

        logger.LogInformation("[ConfirmationService] Auto-approved '{Tool}' after {Timeout}s timeout",
            toolName, config.ConfirmTimeoutSeconds);

        return true;
    }
}
