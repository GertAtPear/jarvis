using Mediahost.Agents.Data;
using Mediahost.Vault.Extensions;
using Rex.Agent.Controllers;
using Rex.Agent.Extensions;

DapperConfig.Configure();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddInfisicalVault(builder.Configuration);

builder.Services.AddRexServices(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
   .WithName("Health");

app.MapRexEndpoints();

app.Run();
