using Jarvis.Api.Data;
using Jarvis.Api.Models;
using Jarvis.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Jarvis.Api.Controllers;

[ApiController]
[Route("api")]
public class ChatController(
    JarvisOrchestratorService orchestrator,
    ConversationService conversation,
    AttachmentService attachments,
    AgentRegistryRepository registry,
    MorningBriefingService briefingService) : ControllerBase
{
    // ── Chat ──────────────────────────────────────────────────────────────────

    [HttpPost("chat")]
    public async Task<ActionResult<OrchestratorResponse>> Chat(
        [FromBody] ChatRequest req,
        CancellationToken ct)
    {
        var sessionId = req.SessionId ?? await conversation.StartSessionAsync();

        var result = await orchestrator.HandleAsync(req.Message, sessionId, ct: ct);
        return Ok(result);
    }

    [HttpPost("chat/upload")]
    public async Task<ActionResult<OrchestratorResponse>> Upload(
        [FromForm] IFormFileCollection files,
        [FromForm] Guid? sessionId,
        [FromForm] string message,
        CancellationToken ct)
    {
        var sid = sessionId ?? await conversation.StartSessionAsync();

        const long maxBytes = 20 * 1024 * 1024;
        var dtos = new List<AttachmentDto>();

        foreach (var file in files)
        {
            if (!attachments.IsAllowedType(file.ContentType))
                return StatusCode(415, $"Unsupported media type: {file.ContentType}");

            if (file.Length > maxBytes)
                return BadRequest($"File '{file.FileName}' exceeds 20 MB limit.");

            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var data = ms.ToArray();

            var record = await attachments.SaveAttachmentAsync(
                sid, file.FileName, file.ContentType, data, null);

            dtos.Add(new AttachmentDto(
                record.Id,
                record.FileName,
                record.MimeType,
                Convert.ToBase64String(data)
            ));
        }

        var result = await orchestrator.HandleAsync(message, sid, dtos, ct);
        return Ok(result);
    }

    [HttpPost("chat/agent/{agentName}")]
    public async Task<ActionResult<OrchestratorResponse>> DirectAgentChat(
        string agentName,
        [FromBody] ChatRequest req,
        CancellationToken ct)
    {
        var sessionId = req.SessionId ?? await conversation.StartSessionAsync();
        var result = await orchestrator.DirectAgentAsync(agentName, req.Message, sessionId, ct);
        if (result is null)
            return NotFound($"Agent '{agentName}' not found or is not active.");
        return Ok(result);
    }

    [HttpGet("chat/{sessionId:guid}/history")]
    public async Task<ActionResult<IEnumerable<AgentMessage>>> History(
        Guid sessionId,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var history = await conversation.GetHistoryAsync(sessionId, limit);
        return Ok(history);
    }

    // ── Sessions ──────────────────────────────────────────────────────────────

    [HttpGet("sessions")]
    public async Task<ActionResult<IEnumerable<SessionSummary>>> Sessions(
        [FromQuery] int count = 30,
        CancellationToken ct = default)
    {
        return Ok(await conversation.GetRecentSessionsAsync(count));
    }

    // ── Agent Registry ────────────────────────────────────────────────────────

    [HttpGet("agents")]
    public async Task<ActionResult<IEnumerable<AgentRecord>>> Agents(CancellationToken ct)
    {
        return Ok(await registry.GetAllAsync());
    }

    [HttpPost("agents/register")]
    public async Task<ActionResult<AgentRecord>> RegisterAgent(
        [FromBody] AgentRecord agent,
        CancellationToken ct)
    {
        await registry.UpsertAgentAsync(agent);
        var created = await registry.GetByNameAsync(agent.Name);
        return Ok(created);
    }

    // ── Status ────────────────────────────────────────────────────────────────

    [HttpGet("status")]
    public async Task<ActionResult<StatusResponse>> Status(CancellationToken ct)
    {
        var agents = await registry.GetActiveAgentsAsync();

        // Parallel health checks — we deliberately don't use IHttpClientFactory here
        // so each check is independent and a slow agent doesn't block others.
        var checks = agents.Select(async a =>
        {
            // Re-resolve factory via DI would require injection — we use the singleton
            // AgentClientFactory which is injected via constructor. But ChatController
            // does not have it injected. Inline a simple check instead.
            var status = a.Status;
            if (!string.IsNullOrEmpty(a.BaseUrl))
            {
                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                    var resp = await client.GetAsync($"{a.BaseUrl.TrimEnd('/')}{a.HealthPath}", ct);
                    status = resp.IsSuccessStatusCode ? "healthy" : "unhealthy";
                }
                catch
                {
                    status = "offline";
                }
            }
            return new AgentStatusItem(a.Name, a.DisplayName, status, a.UpdatedAt);
        });

        var results = await Task.WhenAll(checks);

        return Ok(new StatusResponse("healthy", results));
    }

    // ── Briefing ──────────────────────────────────────────────────────────────

    [HttpGet("briefing/today")]
    public async Task<ActionResult<BriefingResponse>> TodayBriefing(CancellationToken ct)
    {
        var text = await briefingService.GetBriefingIfNeededAsync(ct);
        return Ok(new BriefingResponse(text ?? "No briefing available for today.", text is not null));
    }

    // ── Health ────────────────────────────────────────────────────────────────

    [HttpGet("/health")]
    public IActionResult Health() =>
        Ok(new { status = "healthy", version = "1.0.0" });
}

// ── Request / Response DTOs ───────────────────────────────────────────────────

public record ChatRequest(string Message, Guid? SessionId);

public record StatusResponse(string Jarvis, IEnumerable<AgentStatusItem> Agents);

public record AgentStatusItem(
    string Name,
    string DisplayName,
    string Status,
    DateTimeOffset LastSeen);

public record BriefingResponse(string Content, bool Generated);
