using Lexi.Agent.Data.Repositories;
using Lexi.Agent.Services;
using Mediahost.Agents.Http;
using Microsoft.AspNetCore.Mvc;

namespace Lexi.Agent.Controllers;

public static class LexiEndpoints
{
    public static WebApplication MapLexiEndpoints(this WebApplication app)
    {
        app.MapPost("/api/lexi/chat", async (
            [FromBody] ChatRequest req,
            LexiAgentService agent,
            CancellationToken ct) =>
        {
            var result = await agent.HandleMessageAsync(req.Message, req.SessionId, ct);
            return Results.Ok(new ChatResponse(
                result.Response, result.SessionId, result.ToolCallCount, result.EscalatedFrom));
        });

        app.MapGet("/api/lexi/certificates", async (CertificateRepository repo) =>
            Results.Ok(await repo.GetAllAsync()));

        app.MapGet("/api/lexi/anomalies", async (AnomalyRepository repo) =>
            Results.Ok(await repo.GetUnresolvedAsync()));

        app.MapGet("/api/lexi/cves", async (CveRepository repo) =>
            Results.Ok(await repo.GetUnacknowledgedAsync()));

        app.MapGet("/api/lexi/devices", async (NetworkDeviceRepository repo) =>
            Results.Ok(await repo.GetAllAsync()));

        app.MapGet("/api/lexi/status", async (
            CertificateRepository certRepo,
            AnomalyRepository anomalyRepo,
            CveRepository cveRepo,
            NetworkDeviceRepository deviceRepo) =>
        {
            var expiring  = await certRepo.GetExpiringAsync(30);
            var anomalies = await anomalyRepo.GetUnresolvedAsync();
            var cves      = await cveRepo.GetUnacknowledgedAsync("CRITICAL");
            var unknown   = await deviceRepo.GetUnknownAsync();
            return Results.Ok(new
            {
                status = "healthy",
                expiringCertsIn30Days = expiring.Count(),
                unresolvedAnomalies   = anomalies.Count(),
                criticalCves          = cves.Count(),
                unknownDevices        = unknown.Count()
            });
        });

        return app;
    }
}
