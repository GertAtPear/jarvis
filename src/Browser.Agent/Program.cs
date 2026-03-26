using Browser.Agent.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.FromLogContext()
       .WriteTo.Console()
       .WriteTo.File("logs/browser-.log", rollingInterval: RollingInterval.Day));

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// PlaywrightService is a singleton — it owns the single Chromium process
builder.Services.AddSingleton<PlaywrightService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
app.MapControllers();

// Ensure PlaywrightService initialises (and Chromium launches) at startup
// rather than on the first request.
app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = app.Services.GetRequiredService<PlaywrightService>();
});

// Graceful shutdown: dispose the Playwright browser
app.Lifetime.ApplicationStopping.Register(async () =>
{
    var playwright = app.Services.GetRequiredService<PlaywrightService>();
    await playwright.DisposeAsync();
});

app.Run();
