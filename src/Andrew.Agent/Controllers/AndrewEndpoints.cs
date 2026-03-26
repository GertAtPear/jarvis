using System.Reflection;
using Andrew.Agent.Data.Repositories;
using Andrew.Agent.Models;
using Andrew.Agent.Services;
using Mediahost.Agents.Http;
using Mediahost.Agents.Services;

namespace Andrew.Agent.Controllers;

public static class AndrewEndpoints
{
    public static IEndpointRouteBuilder MapAndrewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/andrew").WithTags("Andrew");

        // POST /api/andrew/servers/register
        group.MapPost("/servers/register", async (
            RegisterServerRequest req,
            ServerRegistrationService registration,
            CancellationToken ct) =>
        {
            var session = await registration.StartRegistrationAsync(
                req.Hostname, req.IpAddress, req.SshPort, req.Notes);
            return Results.Ok(session);
        })
        .WithName("RegisterServer")
        .WithSummary("Register a new server and receive vault credential instructions");

        // POST /api/andrew/servers/{hostname}/activate
        group.MapPost("/servers/{hostname}/activate", async (
            string hostname,
            ServerRegistrationService registration,
            CancellationToken ct) =>
        {
            var result = await registration.ActivateServerAsync(hostname);
            return result.Success ? Results.Ok(result) : Results.UnprocessableEntity(result);
        })
        .WithName("ActivateServer")
        .WithSummary("Test SSH connection and run first discovery for a registered server");

        // POST /api/andrew/chat
        group.MapPost("/chat", async (
            ChatRequest req,
            IAgentService agent,
            CancellationToken ct) =>
        {
            var result = await agent.HandleMessageAsync(req.Message, req.SessionId, ct);
            return Results.Ok(new ChatResponse(result.Response, result.SessionId, result.ToolCallCount));
        })
        .WithName("AndrewChat")
        .WithSummary("Send a message to Andrew");

        // POST /api/andrew/discover/all
        group.MapPost("/discover/all", (
            IServiceScopeFactory scopeFactory,
            ILogger<Program> logger) =>
        {
            var jobId = Guid.NewGuid().ToString("N")[..8];

            _ = Task.Run(async () =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var serverRepo       = scope.ServiceProvider.GetRequiredService<ServerRepository>();
                var sshDiscovery     = scope.ServiceProvider.GetRequiredService<ISshDiscoveryService>();
                var winDiscovery     = scope.ServiceProvider.GetRequiredService<IWindowsDiscoveryService>();

                var servers = await serverRepo.GetAllAsync();
                foreach (var server in servers)
                {
                    try
                    {
                        if (ServerTagHelper.GetConnectionType(server) == "winrm")
                            await winDiscovery.DiscoverServerAsync(server);
                        else
                            await sshDiscovery.DiscoverServerAsync(server);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Discovery failed for {Host}", server.Hostname);
                    }
                }
            });

            return Results.Accepted("/api/andrew/status", new { jobId, message = "Discovery started for all servers" });
        })
        .WithName("DiscoverAll")
        .WithSummary("Trigger full discovery of all servers");

        // POST /api/andrew/discover/{hostname}
        group.MapPost("/discover/{hostname}", async (
            string hostname,
            ServerRepository serverRepo,
            ISshDiscoveryService sshDiscovery,
            IWindowsDiscoveryService winDiscovery,
            CancellationToken ct) =>
        {
            var server = await serverRepo.GetByHostnameAsync(hostname);
            if (server is null)
                return Results.NotFound(new { error = $"Server '{hostname}' not found" });

            var result = ServerTagHelper.GetConnectionType(server) == "winrm"
                ? await winDiscovery.DiscoverServerAsync(server, ct)
                : await sshDiscovery.DiscoverServerAsync(server, ct);
            return Results.Ok(result);
        })
        .WithName("DiscoverServer")
        .WithSummary("Force immediate re-discovery of a single server");

        // GET /api/andrew/servers
        group.MapGet("/servers", async (
            ServerRepository serverRepo) =>
        {
            var list = await serverRepo.GetAllAsync();
            return Results.Ok(list);
        })
        .WithName("ListServers")
        .WithSummary("List all servers from the knowledge store");

        // GET /api/andrew/status
        group.MapGet("/status", async (
            ServerRepository serverRepo,
            DiscoveryLogRepository logRepo) =>
        {
            var allServers = (await serverRepo.GetAllAsync()).ToList();
            var onlineCount = allServers.Count(s => s.Status == "online");
            var recentLog = (await logRepo.GetRecentAsync(1)).FirstOrDefault();

            return Results.Ok(new
            {
                agentName = "Andrew",
                serverCount = allServers.Count,
                onlineCount,
                lastDiscovery = recentLog,
                version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0"
            });
        })
        .WithName("AndrewStatus")
        .WithSummary("Get Andrew's current status and summary");

        // ── Scheduled checks ──────────────────────────────────────────────

        // GET /api/andrew/checks
        group.MapGet("/checks", async (ScheduledCheckRepository checkRepo) =>
        {
            var checks = await checkRepo.GetAllAsync();
            return Results.Ok(checks);
        })
        .WithName("ListChecks")
        .WithSummary("List all scheduled checks");

        // GET /api/andrew/checks/{id}
        group.MapGet("/checks/{id:guid}", async (
            Guid id,
            ScheduledCheckRepository checkRepo) =>
        {
            var check = await checkRepo.GetByIdAsync(id);
            return check is null ? Results.NotFound() : Results.Ok(check);
        })
        .WithName("GetCheck")
        .WithSummary("Get a scheduled check by ID");

        // GET /api/andrew/checks/{id}/history
        group.MapGet("/checks/{id:guid}/history", async (
            Guid id,
            ScheduledCheckRepository checkRepo,
            int limit = 20) =>
        {
            var check = await checkRepo.GetByIdAsync(id);
            if (check is null) return Results.NotFound();

            var history = await checkRepo.GetRecentResultsAsync(id, limit);
            return Results.Ok(new { check, history });
        })
        .WithName("GetCheckHistory")
        .WithSummary("Get recent results for a scheduled check");

        // DELETE /api/andrew/checks/{id}
        group.MapDelete("/checks/{id:guid}", async (
            Guid id,
            ScheduledCheckRepository checkRepo,
            JobSchedulerService scheduler) =>
        {
            var check = await checkRepo.GetByIdAsync(id);
            if (check is null) return Results.NotFound();

            await scheduler.UnscheduleCheckAsync(id);
            await checkRepo.DeleteAsync(id);
            return Results.Ok(new { deleted = true, name = check.Name });
        })
        .WithName("DeleteCheck")
        .WithSummary("Remove a scheduled check");

        // PATCH /api/andrew/checks/{id}/active
        group.MapPatch("/checks/{id:guid}/active", async (
            Guid id,
            SetActiveRequest req,
            ScheduledCheckRepository checkRepo,
            JobSchedulerService scheduler) =>
        {
            var check = await checkRepo.GetByIdAsync(id);
            if (check is null) return Results.NotFound();

            await checkRepo.SetActiveAsync(id, req.Active);

            if (req.Active)
            {
                var updated = (await checkRepo.GetByIdAsync(id))!;
                await scheduler.ScheduleCheckAsync(updated);
            }
            else
            {
                await scheduler.UnscheduleCheckAsync(id);
            }

            return Results.Ok(new { id, active = req.Active });
        })
        .WithName("SetCheckActive")
        .WithSummary("Enable or disable a scheduled check without deleting it");

        return app;
    }
}

public record RegisterServerRequest(string Hostname, string IpAddress, int SshPort = 22, string? Notes = null);
public record SetActiveRequest(bool Active);
