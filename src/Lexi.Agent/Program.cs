using Lexi.Agent.Controllers;
using Lexi.Agent.Extensions;
using Mediahost.Agents.Data;

var builder = WebApplication.CreateBuilder(args);
DapperConfig.Configure();

builder.Services.AddLexiServices(builder.Configuration);

var app = builder.Build();
app.MapLexiEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", agent = "lexi" }));
app.Run();
