using Mediahost.Agents.Data;
using Rocky.Agent.Controllers;
using Rocky.Agent.Extensions;
using Rocky.Agent.Tools;

DapperConfig.Configure();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// ── Scoped services registered via RockyServiceExtensions ─────────────────────
builder.Services.AddRockyServices(builder.Configuration);

// ── Rocky tool executor (scoped — depends on scoped repositories) ─────────────
builder.Services.AddScoped<RockyToolExecutor>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
   .WithName("Health");

app.MapRockyEndpoints();

// ── On startup: load all enabled watched services into Quartz ─────────────────
app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        try
        {
            await RockyServiceExtensions.LoadServiceJobsAsync(app.Services);
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Could not load service jobs on startup (DB may not be ready)");
        }
    });
});

app.Run();
