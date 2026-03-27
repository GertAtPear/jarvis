using Mediahost.Agents.Http;
using Microsoft.AspNetCore.Mvc;
using Nadia.Agent.Data.Repositories;
using Nadia.Agent.Services;

namespace Nadia.Agent.Controllers;

public static class NadiaEndpoints
{
    public static WebApplication MapNadiaEndpoints(this WebApplication app)
    {
        app.MapPost("/api/nadia/chat", async (
            [FromBody] ChatRequest req,
            NadiaAgentService agent,
            CancellationToken ct) =>
        {
            var result = await agent.HandleMessageAsync(req.Message, req.SessionId, ct);
            return Results.Ok(new ChatResponse(
                result.Response, result.SessionId, result.ToolCallCount, result.EscalatedFrom));
        });

        app.MapGet("/api/nadia/interfaces", async (NetworkInterfaceRepository repo) =>
            Results.Ok(await repo.GetAllAsync()));

        app.MapGet("/api/nadia/wifi", async (WifiNodeRepository repo) =>
            Results.Ok(await repo.GetAllAsync()));

        app.MapGet("/api/nadia/status", async (
            NetworkInterfaceRepository ifaceRepo,
            LatencyRepository latencyRepo,
            FailoverRepository failoverRepo) =>
        {
            var interfaces = await ifaceRepo.GetAllAsync();
            var recentFailovers = await failoverRepo.GetRecentAsync(3);
            return Results.Ok(new
            {
                status = "healthy",
                interfaceCount = interfaces.Count(),
                activeInterfaces = interfaces.Count(i => i.IsActive),
                recentFailovers
            });
        });

        return app;
    }
}
