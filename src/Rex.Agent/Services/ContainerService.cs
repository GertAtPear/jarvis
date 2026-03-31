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

    public async Task<(bool Ok, string Output)> LoginRegistryAsync(
        string registry, string username, string password, CancellationToken ct = default)
    {
        _logger.LogInformation("Logging in to registry {Registry} as {User}", registry, username);

        var psi = new System.Diagnostics.ProcessStartInfo(
            "podman", $"login {registry} -u {username} --password-stdin")
        {
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };

        using var proc = System.Diagnostics.Process.Start(psi)!;
        await proc.StandardInput.WriteAsync(password);
        proc.StandardInput.Close();
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        var output = (stdout + stderr).Trim();
        return (proc.ExitCode == 0, output);
    }

    public async Task<(bool Ok, string Output)> PushImageAsync(string tag, CancellationToken ct = default)
    {
        _logger.LogInformation("Pushing image {Tag}", tag);

        var psi = new System.Diagnostics.ProcessStartInfo(
            "podman", $"push {tag}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };

        using var proc = System.Diagnostics.Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        var output = (stdout + stderr).Trim();
        return (proc.ExitCode == 0, output);
    }

    /// <summary>
    /// Executes code in an ephemeral sandboxed container. The container is created with
    /// --rm (auto-removed), no network access, and a 256 MB memory cap.
    ///
    /// Code is base64-encoded and injected as the SANDBOX_CODE environment variable;
    /// each runtime decodes and executes it at startup.
    /// </summary>
    public async Task<SandboxResult> SandboxExecAsync(
        string image,
        string runtime,
        string code,
        Dictionary<string, string>? env,
        int timeoutSeconds,
        bool includeWorkspace,
        string? workspacePath,
        CancellationToken ct = default)
    {
        var encodedCode = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(code));
        var entrypoint  = BuildEntrypoint(runtime);

        var args = new System.Text.StringBuilder("run --rm --network none --memory 256m --cpus 0.5");
        args.Append($" --env SANDBOX_CODE={encodedCode}");

        if (env is not null)
            foreach (var (k, v) in env)
                args.Append($" --env {k}={v}");

        if (includeWorkspace && !string.IsNullOrWhiteSpace(workspacePath))
            args.Append($" --volume {workspacePath}:/workspace:ro");

        args.Append($" {image}");
        args.Append($" {entrypoint}");

        _logger.LogInformation("[sandbox_exec] Running {Runtime} in {Image} (timeout {T}s)", runtime, image, timeoutSeconds);

        var psi = new System.Diagnostics.ProcessStartInfo("podman", args.ToString())
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };

        using var proc = System.Diagnostics.Process.Start(psi)!;

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked     = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        string stdout, stderr;
        try
        {
            stdout = await proc.StandardOutput.ReadToEndAsync(linked.Token);
            stderr = await proc.StandardError.ReadToEndAsync(linked.Token);
            await proc.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return new SandboxResult(false, "", $"Execution timed out after {timeoutSeconds}s", -1);
        }

        _logger.LogInformation("[sandbox_exec] Exit code {Code}", proc.ExitCode);
        return new SandboxResult(proc.ExitCode == 0, stdout.Trim(), stderr.Trim(), proc.ExitCode);
    }

    private static string BuildEntrypoint(string runtime) => runtime.ToLower() switch
    {
        "python" or "python3" =>
            "python3 -c \"import base64,os; exec(base64.b64decode(os.environ['SANDBOX_CODE']).decode())\"",
        "node" or "nodejs" =>
            "node -e \"eval(Buffer.from(process.env.SANDBOX_CODE,'base64').toString())\"",
        "bash" or "sh" =>
            "sh -c 'echo \"$SANDBOX_CODE\" | base64 -d | sh'",
        _ =>
            $"sh -c 'echo \"$SANDBOX_CODE\" | base64 -d | {runtime}'"
    };

    public void Dispose() => _http.Dispose();
}

public record SandboxResult(bool Success, string Stdout, string Stderr, int ExitCode);
