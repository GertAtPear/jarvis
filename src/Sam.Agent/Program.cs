using Mediahost.Agents.Data;
using Sam.Agent.Controllers;
using Sam.Agent.Extensions;

DapperConfig.Configure();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSamServices(builder.Configuration);

var app = builder.Build();
app.MapSamEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", agent = "sam" }));
app.Run();
