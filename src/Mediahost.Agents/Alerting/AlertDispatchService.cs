using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using Dapper;
using Mediahost.Agents.Data;
using Mediahost.Shared.Services;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Mediahost.Agents.Alerting;

public class AlertDispatchService(
    DbConnectionFactory        db,
    IScopedVaultService        vault,
    IConnectionMultiplexer     redisMultiplexer,
    IHttpClientFactory         httpFactory,
    ILogger<AlertDispatchService> logger) : IAlertDispatchService
{
    private static readonly string[] SeverityOrder = ["low", "medium", "high", "critical"];

    private static readonly Dictionary<string, string> SeverityEmoji = new()
    {
        ["critical"] = "🔴",
        ["high"]     = "🟠",
        ["medium"]   = "🟡",
        ["low"]      = "🔵",
    };

    public async Task DispatchAsync(AlertPayload alert, CancellationToken ct = default)
    {
        try
        {
            var channels = await GetActiveChannelsAsync(ct);
            var matching = channels.Where(c => ChannelMatches(c, alert)).ToList();

            if (matching.Count == 0)
            {
                logger.LogDebug("No alert channels matched for {AlertType}/{Severity}", alert.AlertType, alert.Severity);
                return;
            }

            foreach (var channel in matching)
            {
                await DispatchToChannelAsync(channel, alert, ct);
            }
        }
        catch (Exception ex)
        {
            // NEVER throw from DispatchAsync — just log
            logger.LogError(ex, "Alert dispatch failed for {AlertType}", alert.AlertType);
        }
    }

    // ── Channel loading ───────────────────────────────────────────────────────

    private async Task<List<AlertChannel>> GetActiveChannelsAsync(CancellationToken ct)
    {
        var redisDb = redisMultiplexer.GetDatabase();
        const string cacheKey = "alert_channels";

        // Try cache first
        var cached = await redisDb.StringGetAsync(cacheKey);
        if (cached.HasValue)
        {
            try
            {
                return JsonSerializer.Deserialize<List<AlertChannel>>(cached.ToString()) ?? [];
            }
            catch { /* stale cache, re-load */ }
        }

        await using var conn = db.Create();
        var channels = (await conn.QueryAsync<AlertChannel>("""
            SELECT id, channel_name, channel_type, config::text as config_json,
                   min_severity, agent_filter, alert_type_filter, is_active
            FROM jarvis_schema.alert_channels
            WHERE is_active = true
            ORDER BY channel_name
            """)).ToList();

        await redisDb.StringSetAsync(cacheKey,
            JsonSerializer.Serialize(channels), TimeSpan.FromMinutes(5));

        return channels;
    }

    private static bool ChannelMatches(AlertChannel channel, AlertPayload alert)
    {
        // Severity check
        if (!MeetsSeverityThreshold(alert.Severity, channel.MinSeverity))
            return false;

        // Agent filter (null = all agents)
        if (channel.AgentFilter is { Length: > 0 } &&
            !channel.AgentFilter.Any(f => f.Equals(alert.AgentName, StringComparison.OrdinalIgnoreCase)))
            return false;

        // Alert type filter (null = all types)
        if (channel.AlertTypeFilter is { Length: > 0 } &&
            !channel.AlertTypeFilter.Any(f => f.Equals(alert.AlertType, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }

    private static bool MeetsSeverityThreshold(string alertSeverity, string? minSeverity)
    {
        if (string.IsNullOrEmpty(minSeverity)) return true;
        var alertIdx = Array.IndexOf(SeverityOrder, alertSeverity.ToLower());
        var minIdx   = Array.IndexOf(SeverityOrder, minSeverity.ToLower());
        return alertIdx >= minIdx;
    }

    // ── Dispatch per channel ──────────────────────────────────────────────────

    private async Task DispatchToChannelAsync(AlertChannel channel, AlertPayload alert, CancellationToken ct)
    {
        bool   delivered = false;
        string? error    = null;

        try
        {
            if (channel.ChannelType == "slack")
            {
                await DispatchSlackAsync(channel, alert, ct);
                delivered = true;
            }
            else if (channel.ChannelType == "email")
            {
                await DispatchEmailAsync(channel, alert, ct);
                delivered = true;
            }
            else
            {
                error = $"Unknown channel type: {channel.ChannelType}";
                logger.LogWarning("Unknown channel type '{Type}' for channel '{Name}'", channel.ChannelType, channel.ChannelName);
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            logger.LogError(ex, "Failed to dispatch to channel '{Name}'", channel.ChannelName);
        }

        await LogDispatchAsync(alert, channel, delivered, error);
    }

    private async Task DispatchSlackAsync(AlertChannel channel, AlertPayload alert, CancellationToken ct)
    {
        var configDoc   = JsonDocument.Parse(channel.ConfigJson ?? "{}");
        var secretPath  = configDoc.RootElement.TryGetProperty("webhook_url_secret", out var sp)
            ? sp.GetString() : null;

        if (string.IsNullOrEmpty(secretPath))
            throw new InvalidOperationException("Slack channel config missing webhook_url_secret");

        var webhookUrl = await vault.GetSecretAsync(secretPath, "webhook_url", ct)
            ?? throw new InvalidOperationException($"Webhook URL not found at vault path: {secretPath}");

        var emoji    = SeverityEmoji.GetValueOrDefault(alert.Severity.ToLower(), "⚪");
        var ts       = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");

        var blocks = new object[]
        {
            new { type = "section", text = new { type = "mrkdwn",
                text = $"{emoji} *{alert.Title}*" } },
            new { type = "section", text = new { type = "mrkdwn",
                text = alert.Body.Length > 300 ? alert.Body[..300] + "…" : alert.Body } },
            new { type = "context", elements = new object[]
            {
                new { type = "mrkdwn", text = $"Agent: *{alert.AgentName}* | Type: `{alert.AlertType}` | Severity: *{alert.Severity}* | {ts}" }
            }},
        };

        object payload = alert.SourceUrl is not null
            ? new { blocks, attachments = new[] { new { actions = new[] {
                new { type = "button", text = "View Details", url = alert.SourceUrl } } } } }
            : new { blocks };

        var http = httpFactory.CreateClient();
        var resp = await http.PostAsJsonAsync(webhookUrl, payload, ct);
        resp.EnsureSuccessStatusCode();

        logger.LogInformation("Alert dispatched to Slack channel '{Name}'", channel.ChannelName);
    }

    private async Task DispatchEmailAsync(AlertChannel channel, AlertPayload alert, CancellationToken ct)
    {
        var configDoc  = JsonDocument.Parse(channel.ConfigJson ?? "{}");
        var smtpSecret = configDoc.RootElement.TryGetProperty("smtp_secret", out var ss)
            ? ss.GetString() : null;
        var toAddresses = configDoc.RootElement.TryGetProperty("to", out var toEl)
            ? toEl.EnumerateArray().Select(e => e.GetString()!).ToArray()
            : [];

        if (string.IsNullOrEmpty(smtpSecret))
            throw new InvalidOperationException("Email channel config missing smtp_secret");
        if (toAddresses.Length == 0)
            throw new InvalidOperationException("Email channel config missing 'to' array");

        var smtpHost  = await vault.GetSecretAsync(smtpSecret, "host", ct) ?? "localhost";
        var smtpPort  = int.Parse(await vault.GetSecretAsync(smtpSecret, "port", ct) ?? "587");
        var smtpUser  = await vault.GetSecretAsync(smtpSecret, "username", ct);
        var smtpPass  = await vault.GetSecretAsync(smtpSecret, "password", ct);
        var fromAddr  = await vault.GetSecretAsync(smtpSecret, "from", ct) ?? "alerts@mediahost.co.za";

        var subject = $"[{alert.Severity.ToUpper()}] {alert.Title} — Mediahost Alert";
        var body    = $"""
            {alert.Title}

            {alert.Body}

            ---
            Agent: {alert.AgentName}
            Alert Type: {alert.AlertType}
            Severity: {alert.Severity}
            Time: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss UTC}
            {(alert.SourceUrl is not null ? $"\nDetails: {alert.SourceUrl}" : "")}
            """;

        using var client = new SmtpClient(smtpHost, smtpPort);
        if (smtpUser is not null && smtpPass is not null)
        {
            client.Credentials = new System.Net.NetworkCredential(smtpUser, smtpPass);
            client.EnableSsl   = true;
        }

        using var msg = new MailMessage();
        msg.From = new MailAddress(fromAddr);
        foreach (var to in toAddresses)
            msg.To.Add(to);
        msg.Subject = subject;
        msg.Body    = body;

        await client.SendMailAsync(msg, ct);
        logger.LogInformation("Alert dispatched via email to {Count} recipient(s)", toAddresses.Length);
    }

    private async Task LogDispatchAsync(AlertPayload alert, AlertChannel channel, bool delivered, string? error)
    {
        try
        {
            await using var conn = db.Create();
            await conn.ExecuteAsync("""
                INSERT INTO jarvis_schema.alert_dispatch_log
                    (alert_id, agent_name, alert_type, severity, channel_id, channel_type, delivered, error_message)
                VALUES
                    (@alertId, @agentName, @alertType, @severity, @channelId, @channelType, @delivered, @error)
                """, new
            {
                alertId     = alert.AlertId,
                agentName   = alert.AgentName,
                alertType   = alert.AlertType,
                severity    = alert.Severity,
                channelId   = channel.Id,
                channelType = channel.ChannelType,
                delivered,
                error
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to log alert dispatch");
        }
    }
}

// ── Internal model ─────────────────────────────────────────────────────────────

internal class AlertChannel
{
    public Guid     Id                { get; init; }
    public string   ChannelName       { get; init; } = "";
    public string   ChannelType       { get; init; } = "";
    public string?  ConfigJson        { get; init; }
    public string?  MinSeverity       { get; init; } = "high";
    public string[]? AgentFilter      { get; init; }
    public string[]? AlertTypeFilter  { get; init; }
    public bool     IsActive          { get; init; }
}
