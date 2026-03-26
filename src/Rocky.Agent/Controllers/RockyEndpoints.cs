using Mediahost.Agents.Http;
using Mediahost.Agents.Services;
using Rocky.Agent.Data.Repositories;
using Rocky.Agent.Services;

namespace Rocky.Agent.Controllers;

public static class RockyEndpoints
{
    public static IEndpointRouteBuilder MapRockyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rocky").WithTags("Rocky");

        // POST /api/rocky/chat
        group.MapPost("/chat", async (
            ChatRequest req,
            IAgentService agent,
            CancellationToken ct) =>
        {
            var result = await agent.HandleMessageAsync(req.Message, req.SessionId, ct);
            return Results.Ok(new ChatResponse(result.Response, result.SessionId, result.ToolCallCount));
        })
        .WithName("RockyChat")
        .WithSummary("Send a message to Rocky");

        // GET /api/rocky/services
        group.MapGet("/services", async (WatchedServiceRepository repo) =>
        {
            var services = await repo.GetAllAsync();
            return Results.Ok(services);
        })
        .WithName("ListWatchedServices")
        .WithSummary("List all watched services");

        // GET /api/rocky/services/{name}/status
        group.MapGet("/services/{name}/status", async (
            string name,
            WatchedServiceRepository serviceRepo,
            CheckResultRepository checkRepo) =>
        {
            var service = await serviceRepo.GetByNameAsync(name);
            if (service is null) return Results.NotFound(new { error = $"Service '{name}' not found" });

            var latest = await checkRepo.GetLatestAsync(service.Id);
            return Results.Ok(new { service, latest });
        })
        .WithName("GetServiceStatus")
        .WithSummary("Get current health status for a service");

        // GET /api/rocky/services/{name}/history
        group.MapGet("/services/{name}/history", async (
            string name,
            WatchedServiceRepository serviceRepo,
            CheckResultRepository checkRepo,
            int limit = 50) =>
        {
            var service = await serviceRepo.GetByNameAsync(name);
            if (service is null) return Results.NotFound(new { error = $"Service '{name}' not found" });

            var history = await checkRepo.GetRecentAsync(service.Id, Math.Min(limit, 200));
            return Results.Ok(new { service, history });
        })
        .WithName("GetServiceHistory")
        .WithSummary("Get recent check history for a service");

        // GET /api/rocky/alerts
        group.MapGet("/alerts", async (AlertRepository alertRepo) =>
        {
            var alerts = await alertRepo.GetUnresolvedAsync();
            return Results.Ok(alerts);
        })
        .WithName("GetActiveAlerts")
        .WithSummary("Get all currently unresolved alerts");

        // GET /api/rocky/alerts/recent
        group.MapGet("/alerts/recent", async (
            AlertRepository alertRepo,
            int limit = 50) =>
        {
            var alerts = await alertRepo.GetRecentAsync(Math.Min(limit, 200));
            return Results.Ok(alerts);
        })
        .WithName("GetRecentAlerts")
        .WithSummary("Get recent alerts (including resolved)");

        // GET /api/rocky/status
        group.MapGet("/status", async (WatchedServiceRepository serviceRepo) =>
        {
            var all     = (await serviceRepo.GetAllAsync()).ToList();
            var enabled = all.Count(s => s.Enabled);
            return Results.Ok(new
            {
                agentName    = "Rocky",
                totalServices = all.Count,
                enabledServices = enabled,
                version      = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0"
            });
        })
        .WithName("RockyStatus")
        .WithSummary("Get Rocky's current operational status");

        return app;
    }
}
