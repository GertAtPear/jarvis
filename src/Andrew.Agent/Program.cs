using Andrew.Agent.Controllers;
using Andrew.Agent.Data.Repositories;
using Andrew.Agent.Extensions;
using Andrew.Agent.Services;
using Mediahost.Agents.Data;
using Mediahost.Vault.Extensions;

DapperConfig.Configure();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddInfisicalVault(builder.Configuration);
builder.Services.AddRepositories();

// Named HTTP clients used by jobs and check executors
builder.Services.AddHttpClient("network-check", c =>
{
    c.Timeout = TimeSpan.FromSeconds(8);
    c.DefaultRequestHeaders.Add("User-Agent", "Andrew-Agent/1.0");
});
builder.Services.AddHttpClient("website-check", c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
    c.DefaultRequestHeaders.Add("User-Agent", "Andrew-Agent/1.0");
});

// Quartz scheduler (registers ISchedulerFactory + built-in jobs)
builder.Services.AddAndrewScheduler(builder.Configuration);

// Agent services (registers JobSchedulerService, executors, tools, LlmService, etc.)
builder.Services.AddAgentServices(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
   .WithName("Health");

app.MapAndrewEndpoints();

// ── On startup: load persisted custom checks into the Quartz scheduler ────────
app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        try
        {
            await using var scope = app.Services.CreateAsyncScope();
            var checkRepo  = scope.ServiceProvider.GetRequiredService<ScheduledCheckRepository>();
            var scheduler  = scope.ServiceProvider.GetRequiredService<JobSchedulerService>();
            var logger     = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

            var checks = (await checkRepo.GetAllActiveAsync()).ToList();
            if (checks.Count > 0)
            {
                await scheduler.LoadChecksAsync(checks);
            }
            else
            {
                logger.LogInformation(
                    "Andrew is ready. No scheduled checks configured yet. " +
                    "Tell Jarvis: 'Andrew, check if the redis container is running every 10 minutes'");
            }
        }
        catch (Exception ex)
        {
            // Non-fatal — DB may not be ready yet; checks will still be loaded
            // next time they are created
            app.Logger.LogWarning(ex, "Could not load scheduled checks on startup (DB may not be ready)");
        }
    });
});

app.Run();
