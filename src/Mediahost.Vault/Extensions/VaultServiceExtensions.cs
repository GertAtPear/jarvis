using Mediahost.Shared.Services;
using Mediahost.Vault.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mediahost.Vault.Extensions;

public static class VaultServiceExtensions
{
    public static IServiceCollection AddInfisicalVault(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var baseUrl = configuration["Infisical:BaseUrl"]
            ?? throw new InvalidOperationException("Infisical:BaseUrl is required");

        services.AddHttpClient("infisical", client =>
            {
                client.BaseAddress = new Uri(baseUrl);
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<RetryHandler>();

        services.AddTransient<RetryHandler>();
        services.AddSingleton<IVaultService, InfisicalVaultService>();

        return services;
    }
}

/// <summary>
/// Retries transient HTTP failures with exponential backoff: 1 s, 2 s, 4 s.
/// Buffers request content so POST bodies can be replayed on retry.
/// </summary>
internal sealed class RetryHandler : DelegatingHandler
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
    ];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buffer the body once so it can be re-sent on each retry attempt.
        byte[]? body = null;
        string? mediaType = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            mediaType = request.Content.Headers.ContentType?.ToString();
        }

        for (int attempt = 0; ; attempt++)
        {
            // Restore content for every attempt (content stream is consumed after the first send).
            if (body is not null)
            {
                request.Content = new ByteArrayContent(body);
                if (mediaType is not null)
                    request.Content.Headers.ContentType =
                        System.Net.Http.Headers.MediaTypeHeaderValue.Parse(mediaType);
            }

            try
            {
                var response = await base.SendAsync(request, cancellationToken);

                if (!IsTransientStatus(response.StatusCode) || attempt >= Delays.Length)
                    return response;

                response.Dispose();
            }
            catch (Exception ex) when (IsTransientException(ex) && attempt < Delays.Length) { }

            await Task.Delay(Delays[attempt], cancellationToken);
        }
    }

    private static bool IsTransientStatus(System.Net.HttpStatusCode status) =>
        (int)status >= 500 || status == System.Net.HttpStatusCode.RequestTimeout;

    private static bool IsTransientException(Exception ex) =>
        ex is HttpRequestException
            or TimeoutException
            or TaskCanceledException { InnerException: TimeoutException };
}
