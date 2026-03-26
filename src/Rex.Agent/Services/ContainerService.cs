using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rex.Agent.Services;

public class ContainerSummary
{
    [JsonPropertyName("Id")]      public string Id      { get; init; } = "";
    [JsonPropertyName("Names")]   public string[] Names { get; init; } = [];
    [JsonPropertyName("Image")]   public string Image   { get; init; } = "";
    [JsonPropertyName("State")]   public string State   { get; init; } = "";
    [JsonPropertyName("Status")]  public string Status  { get; init; } = "";
}

public class ContainerService : IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<ContainerService> _logger;

    public ContainerService(IConfiguration config, ILogger<ContainerService> logger)
    {
        _logger = logger;
        var socketPath = config["Rex:PodmanSocket"] ?? "/run/podman/podman.sock";

        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (ctx, ct) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
        };
        _http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
    }

    public async Task<List<ContainerSummary>> ListContainersAsync(CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<List<ContainerSummary>>(
            "/v4.0.0/libpod/containers/json?all=true", ct);
        return result ?? [];
    }

    public async Task<string> GetLogsAsync(string containerName, int tail = 100, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync(
            $"/v4.0.0/libpod/containers/{containerName}/logs?stdout=true&stderr=true&tail={tail}", ct);
        return await resp.Content.ReadAsStringAsync(ct);
    }

    public async Task<(bool Ok, string Output)> BuildImageAsync(string dockerfile, string contextPath, string tag, CancellationToken ct = default)
    {
        // Build via podman CLI for simplicity (REST API for build requires tar archive)
        _logger.LogInformation("Building image {Tag} from {Context}", tag, contextPath);

        var psi = new System.Diagnostics.ProcessStartInfo(
            "podman", $"build -f {dockerfile} -t {tag} {contextPath}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };

        using var proc = System.Diagnostics.Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        var output = stdout + stderr;
        return (proc.ExitCode == 0, output);
    }

    public async Task RestartContainerAsync(string containerName, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync(
            $"/v4.0.0/libpod/containers/{containerName}/restart", null, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<string> InspectContainerAsync(string containerName, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync(
            $"/v4.0.0/libpod/containers/{containerName}/json", ct);
        return await resp.Content.ReadAsStringAsync(ct);
    }

    public void Dispose() => _http.Dispose();
}
