using LaptopHost.Modules;
using LaptopHost.Platform;
using LaptopHost.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

// ── Logging (before DI) ───────────────────────────────────────────────────────

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/laptop-host-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7)
    .CreateLogger();

// ── CLI handling ──────────────────────────────────────────────────────────────
// Note: 'args' is the implicit top-level statements parameter for command-line args

if (args.Contains("--install") || args.Contains("-i"))
{
    LinuxDaemon.PrintSystemdUnit(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
        ?? "/usr/local/bin/laptop-host");
    return;
}

// ── Configuration ─────────────────────────────────────────────────────────────

var configPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".laptophost", "laptophost.json");

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile(configPath, optional: true)
    .AddEnvironmentVariables("LAH_")
    .AddCommandLine(args)
    .Build();

var lahConfig = new LahConfig
{
    JarvisBaseUrl          = configuration["JarvisBaseUrl"] ?? "http://localhost:5000",
    DeviceId               = configuration["DeviceId"]      ?? "",
    DeviceToken            = configuration["DeviceToken"]   ?? "",
    DeviceName             = configuration["DeviceName"]    ?? Environment.MachineName,
    ConfirmTimeoutSeconds  = configuration.GetValue<int>("ConfirmTimeoutSeconds", 30)
};

// ── Registration flow (--register <token>) ───────────────────────────────────

var registerIdx = Array.IndexOf(args, "--register");
if (registerIdx == -1) registerIdx = Array.IndexOf(args, "-r");

if (registerIdx >= 0 && registerIdx + 1 < args.Length)
{
    var token       = args[registerIdx + 1];
    var jarvisUrl   = configuration["JarvisBaseUrl"] ?? lahConfig.JarvisBaseUrl;
    var deviceName  = configuration["DeviceName"] ?? Environment.MachineName;

    await RegisterDeviceAsync(jarvisUrl, deviceName, token, configPath);
    return;
}

// ── Normal startup ────────────────────────────────────────────────────────────

if (!lahConfig.IsRegistered)
{
    Log.Fatal("[LAH] Not registered. Run: laptop-host --register <token>");
    Log.Fatal("[LAH] Get a token from Jarvis → Settings → Devices → Add Device");
    Log.CloseAndFlush();
    return;
}

Log.Information("[LAH] Starting Mediahost AI Local Agent Host v1.0");
Log.Information("[LAH] Device: {Name} ({Id})", lahConfig.DeviceName, lahConfig.DeviceId);
Log.Information("[LAH] Jarvis: {Url}", lahConfig.JarvisBaseUrl);

var host = Host.CreateDefaultBuilder(args)
    .UseSerilog()
    .ConfigureServices(services =>
    {
        // Config
        services.AddSingleton(lahConfig);

        // Modules
        services.AddSingleton<ILaptopToolModule, FileSystemModule>();
        services.AddSingleton<ILaptopToolModule, PodmanModule>();
        services.AddSingleton<ILaptopToolModule, GitModule>();
        services.AddSingleton<ILaptopToolModule, SystemModule>();
        services.AddSingleton<ILaptopToolModule, AppLauncherModule>();

        // Services
        services.AddSingleton<ConfirmationService>();
        services.AddSingleton<ToolDispatchService>();
        services.AddHostedService<JarvisConnectionService>();
    })
    .Build();

await host.RunAsync();

Log.CloseAndFlush();

// ── Registration helper ───────────────────────────────────────────────────────

static async Task RegisterDeviceAsync(string jarvisUrl, string deviceName, string regToken, string configPath)
{
    Log.Information("[LAH] Storing registration token for device '{Name}'...", deviceName);

    try
    {
        var dir = Path.GetDirectoryName(configPath)!;
        Directory.CreateDirectory(dir);

        var configJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            JarvisBaseUrl = jarvisUrl,
            DeviceToken   = regToken,
            DeviceName    = deviceName
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        await File.WriteAllTextAsync(configPath, configJson);

        Log.Information("[LAH] Registration successful!");
        Log.Information("[LAH] Config: {Path}", configPath);
        Log.Information("[LAH] Start the service: laptop-host");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "[LAH] Registration failed");
    }
    finally
    {
        Log.CloseAndFlush();
    }
}

