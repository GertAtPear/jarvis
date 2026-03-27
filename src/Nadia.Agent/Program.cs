using Mediahost.Agents.Data;
using Nadia.Agent.Controllers;
using Nadia.Agent.Extensions;

DapperConfig.Configure();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNadiaServices(builder.Configuration);

var app = builder.Build();
app.MapNadiaEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", agent = "nadia" }));
app.Run();
