using Eve.Agent.Controllers;
using Eve.Agent.Extensions;
using Mediahost.Agents.Data;
using Mediahost.Vault.Extensions;
using Serilog;

DapperConfig.Configure();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.FromLogContext()
       .WriteTo.Console()
       .WriteTo.File("logs/eve-.log", rollingInterval: RollingInterval.Day));

builder.Services.AddOpenApi();
builder.Services.AddHttpClient();

builder.Services.AddInfisicalVault(builder.Configuration);
builder.Services.AddRepositories();
builder.Services.AddEveServices(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
   .WithName("Health");

app.MapEveEndpoints();

app.Run();
