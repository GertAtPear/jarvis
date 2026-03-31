using Mediahost.Agents.Http;
using Microsoft.AspNetCore.Mvc;
using Research.Agent.Data.Repositories;
using Research.Agent.Services;

namespace Research.Agent.Controllers;

public static class ResearchEndpoints
{
    public static WebApplication MapResearchEndpoints(this WebApplication app)
    {
        app.MapPost("/api/research/chat", async (
            [FromBody] ChatRequest req,
            ResearchAgentService agent,
            CancellationToken ct) =>
        {
            var result = await agent.HandleMessageAsync(req.Message, req.SessionId, ct);
            return Results.Ok(new ChatResponse(
                result.Response, result.SessionId, result.ToolCallCount, result.EscalatedFrom));
        });

        app.MapGet("/api/research/databases", async (ResearchDatabaseRepository repo) =>
            Results.Ok(await repo.GetAllActiveAsync()));

        return app;
    }
}
