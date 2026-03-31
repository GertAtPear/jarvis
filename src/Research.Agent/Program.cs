using Mediahost.Agents.Data;
using Research.Agent.Controllers;
using Research.Agent.Extensions;

DapperConfig.Configure();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddResearchServices(builder.Configuration);

var app = builder.Build();
app.MapResearchEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", agent = "research" }));
app.Run();
