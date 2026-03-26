using System.Reflection;
using Mediahost.Agents.Http;
using Mediahost.Agents.Services;
using Rex.Agent.Data.Repositories;

namespace Rex.Agent.Controllers;

public static class RexEndpoints
{
    public static IEndpointRouteBuilder MapRexEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rex").WithTags("Rex");

        // POST /api/rex/chat
        group.MapPost("/chat", async (
            ChatRequest req,
            IAgentService agent,
            CancellationToken ct) =>
        {
            var result = await agent.HandleMessageAsync(req.Message, req.SessionId, ct);
            return Results.Ok(new ChatResponse(result.Response, result.SessionId, result.ToolCallCount));
        })
        .WithName("RexChat")
        .WithSummary("Send a message to Rex");

        // GET /api/rex/status
        group.MapGet("/status", async (ProjectRepository projects) =>
        {
            var allProjects = await projects.GetAllAsync();
            return Results.Ok(new
            {
                agentName    = "Rex",
                projectCount = allProjects.Count,
                projects     = allProjects.Select(p => new { p.Name, p.Language, p.Status }).ToList(),
                version      = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0"
            });
        })
        .WithName("RexStatus")
        .WithSummary("Get Rex's current status");

        // GET /api/rex/projects
        group.MapGet("/projects", async (ProjectRepository projects) =>
        {
            var list = await projects.GetAllAsync();
            return Results.Ok(list);
        })
        .WithName("ListProjects")
        .WithSummary("List all tracked projects");

        // GET /api/rex/dev-sessions
        group.MapGet("/dev-sessions", async (
            DevSessionRepository devSessions,
            int limit = 20) =>
        {
            var list = await devSessions.GetRecentAsync(limit);
            return Results.Ok(list);
        })
        .WithName("ListDevSessions")
        .WithSummary("List recent developer agent sessions");

        return app;
    }
}
