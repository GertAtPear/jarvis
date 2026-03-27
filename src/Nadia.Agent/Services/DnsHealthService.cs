using System.Diagnostics;
using System.Net;
using Nadia.Agent.Data.Repositories;

namespace Nadia.Agent.Services;

public class DnsHealthService(
    DnsCheckRepository dnsRepo,
    ILogger<DnsHealthService> logger)
{
    private static readonly (string Resolver, string Query)[] DefaultChecks =
    [
        ("8.8.8.8",   "google.com"),
        ("1.1.1.1",   "cloudflare.com"),
        ("127.0.0.53", "mediahost.local")
    ];

    public async Task CheckAllAsync(CancellationToken ct = default)
    {
        foreach (var (resolver, query) in DefaultChecks)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(query, ct);
                sw.Stop();
                var result = string.Join(", ", addresses.Take(2).Select(a => a.ToString()));
                await dnsRepo.InsertAsync(resolver, "A", query, result, (int)sw.ElapsedMilliseconds, true);
            }
            catch (Exception ex)
            {
                sw.Stop();
                logger.LogWarning(ex, "[Nadia] DNS check failed for {Query} via {Resolver}", query, resolver);
                await dnsRepo.InsertAsync(resolver, "A", query, null, (int)sw.ElapsedMilliseconds, false);
            }
        }
    }
}
