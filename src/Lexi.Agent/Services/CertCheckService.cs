using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Lexi.Agent.Data.Repositories;

namespace Lexi.Agent.Services;

public class CertCheckService(
    CertificateRepository certRepo,
    ILogger<CertCheckService> logger)
{
    public async Task<string> CheckSingleAsync(string host, int port = 443, CancellationToken ct = default)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, ct);

            using var ssl = new SslStream(client.GetStream(), false,
                (_, _, _, _) => true); // accept any cert for inspection

            await ssl.AuthenticateAsClientAsync(host);
            var cert = ssl.RemoteCertificate as X509Certificate2
                       ?? new X509Certificate2(ssl.RemoteCertificate!);

            var daysRemaining = (int)(cert.NotAfter - DateTime.UtcNow).TotalDays;
            var sans = new List<string>();
            var sanExt = cert.Extensions["2.5.29.17"];
            if (sanExt is not null)
                sans.Add(sanExt.Format(false));
            var sanJson = JsonSerializer.Serialize(sans);

            await certRepo.UpsertAsync(
                host, port,
                cert.GetNameInfo(X509NameType.SimpleName, false),
                cert.Issuer,
                sanJson,
                cert.NotBefore,
                cert.NotAfter,
                daysRemaining,
                daysRemaining > 0);

            return JsonSerializer.Serialize(new
            {
                host, port,
                subjectCn = cert.GetNameInfo(X509NameType.SimpleName, false),
                issuer = cert.Issuer,
                validFrom = cert.NotBefore,
                validTo = cert.NotAfter,
                daysRemaining,
                isValid = daysRemaining > 0
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Lexi] TLS check failed for {Host}:{Port}", host, port);
            await certRepo.UpsertAsync(host, port, null, null, null, null, null, null, false);
            return $"{{\"error\": \"{ex.Message.Replace("\"", "'")}\", \"host\": \"{host}\", \"port\": {port}}}";
        }
    }
}
