using System.Net.NetworkInformation;
using Nadia.Agent.Data.Repositories;

namespace Nadia.Agent.Services;

public class LatencyProbeService(
    NetworkInterfaceRepository ifaceRepo,
    LatencyRepository latencyRepo,
    ILogger<LatencyProbeService> logger)
{
    private static readonly string[] DefaultTargets = ["8.8.8.8", "1.1.1.1", "8.8.4.4"];

    public async Task<(double RttMs, double PacketLossPct)> PingAsync(string host, CancellationToken ct = default)
    {
        const int attempts = 4;
        var successful = new List<double>();

        for (var i = 0; i < attempts; i++)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(host, 2000);
                if (reply.Status == IPStatus.Success)
                    successful.Add(reply.RoundtripTime);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "[Nadia] Ping attempt {i} to {host} failed", i, host);
            }
        }

        var loss = (attempts - successful.Count) / (double)attempts * 100;
        var avg  = successful.Count > 0 ? successful.Average() : -1;
        return (avg, loss);
    }

    public async Task ProbeAllInterfacesAsync(CancellationToken ct = default)
    {
        var interfaces = await ifaceRepo.GetAllAsync();

        foreach (var iface in interfaces.Where(i => i.IsActive))
        {
            foreach (var target in DefaultTargets)
            {
                try
                {
                    var (rtt, loss) = await PingAsync(target, ct);
                    if (rtt >= 0)
                        await latencyRepo.InsertAsync(iface.Id, target, rtt, loss);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[Nadia] Failed to probe {Target} for interface {Iface}", target, iface.Name);
                }
            }
        }
    }
}
