using Microsoft.AspNetCore.Mvc;
using Mediahost.Agents.Http;
using Sam.Agent.Data.Repositories;
using Sam.Agent.Services;

namespace Sam.Agent.Controllers;

public static class SamEndpoints
{
    public static WebApplication MapSamEndpoints(this WebApplication app)
    {
        app.MapPost("/api/sam/chat", async (
            [FromBody] ChatRequest req,
            SamAgentService agent,
            CancellationToken ct) =>
        {
            var result = await agent.HandleMessageAsync(req.Message, req.SessionId, ct);
            return Results.Ok(new ChatResponse(
                result.Response, result.SessionId, result.ToolCallCount, result.EscalatedFrom));
        });

        app.MapGet("/api/sam/databases", async (DatabaseRepository repo) =>
            Results.Ok(await repo.GetAllAsync()));

        app.MapGet("/api/sam/databases/{name}/health", async (
            string name,
            DatabaseRepository databaseRepo,
            ConnectionStatsRepository connRepo,
            ReplicationRepository replRepo,
            SlowQueryRepository slowRepo) =>
        {
            var db = await databaseRepo.GetByNameAsync(name);
            if (db is null) return Results.NotFound($"Database '{name}' not found");
            var conns = await connRepo.GetLatestAsync(db.Id);
            var repl  = await replRepo.GetLatestAsync(db.Id);
            var slow  = await slowRepo.GetRecentAsync(db.Id, 5);
            return Results.Ok(new { database = db, connections = conns, replication = repl, topSlowQueries = slow });
        });

        app.MapGet("/api/sam/status", async (DatabaseRepository repo) =>
        {
            var dbs = await repo.GetAllAsync();
            return Results.Ok(new
            {
                status = "healthy",
                databases = dbs.Count(),
                healthy = dbs.Count(d => d.Status == "healthy"),
                unhealthy = dbs.Count(d => d.Status == "error")
            });
        });

        return app;
    }
}
