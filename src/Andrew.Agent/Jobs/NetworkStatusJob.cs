using System.Net.Sockets;
using Andrew.Agent.Data.Repositories;
using Quartz;

namespace Andrew.Agent.Jobs;

/// <summary>
/// Runs every 5 minutes. Checks VPN connectivity, active internet line, and DNS.
/// Results stored in andrew_schema.network_facts for instant querying.
/// </summary>
[DisallowConcurrentExecution]
public class NetworkStatusJob(
    NetworkFactRepository networkFacts,
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<NetworkStatusJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;

        await Task.WhenAll(
            CheckVpnAsync(ct),
            CheckInternetLineAsync(ct),
            CheckDnsAsync(ct));
    }

    // ── VPN ──────────────────────────────────────────────────────────────────

    private async Task CheckVpnAsync(CancellationToken ct)
    {
        var gatewayIp = config["Andrew:Network:VpnGatewayIp"];
        if (string.IsNullOrEmpty(gatewayIp))
        {
            logger.LogDebug("VPN check skipped — Andrew:Network:VpnGatewayIp not configured");
            return;
        }

        var up = await TcpConnectAsync(gatewayIp, 443, TimeSpan.FromSeconds(3));

        await networkFacts.SetFactAsync("vpn_status", new
        {
            up,
            gateway = gatewayIp,
            checked_at = DateTime.UtcNow
        });

        if (!up)
            logger.LogWarning("VPN appears down — TCP connect to {Gateway}:443 failed", gatewayIp);
    }

    // ── Internet line ─────────────────────────────────────────────────────────

    private async Task CheckInternetLineAsync(CancellationToken ct)
    {
        var primaryIp = config["Andrew:Network:PrimaryPublicIp"];
        string? detectedIp = null;
        var attempts = 0;

        while (attempts < 2 && detectedIp is null)
        {
            try
            {
                var client = httpClientFactory.CreateClient("network-check");
                var response = await client.GetStringAsync("https://api.ipify.org?format=json", ct);
                var doc = System.Text.Json.JsonDocument.Parse(response);
                detectedIp = doc.RootElement.GetProperty("ip").GetString();
            }
            catch (Exception ex)
            {
                attempts++;
                if (attempts < 2)
                {
                    logger.LogDebug("ipify check attempt {N} failed: {Msg}", attempts, ex.Message);
                    await Task.Delay(2000, ct);
                }
            }
        }

        string line;
        if (detectedIp is null)
        {
            line = "offline";
        }
        else if (string.IsNullOrEmpty(primaryIp))
        {
            line = "unknown"; // primary IP not configured, can't determine which line
        }
        else
        {
            line = detectedIp == primaryIp ? "primary" : "backup";
        }

        await networkFacts.SetFactAsync("active_line", new
        {
            line,
            public_ip = detectedIp ?? "unreachable",
            checked_at = DateTime.UtcNow
        });

        if (line == "offline")
            logger.LogWarning("Internet appears offline — could not reach ipify.org");
        else if (line == "backup")
            logger.LogWarning("Running on BACKUP internet line (detected IP: {Ip})", detectedIp);
    }

    // ── DNS ───────────────────────────────────────────────────────────────────

    private async Task CheckDnsAsync(CancellationToken ct)
    {
        bool resolving;
        try
        {
            await System.Net.Dns.GetHostAddressesAsync("google.com", ct);
            resolving = true;
        }
        catch
        {
            resolving = false;
            logger.LogWarning("DNS resolution failed — network may be severely degraded");
        }

        await networkFacts.SetFactAsync("dns_status", new
        {
            resolving,
            checked_at = DateTime.UtcNow
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<bool> TcpConnectAsync(string host, int port, TimeSpan timeout)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(timeout));
            return completed == connectTask && client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
