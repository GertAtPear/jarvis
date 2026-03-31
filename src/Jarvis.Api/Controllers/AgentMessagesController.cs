using Jarvis.Api.Data;
using Microsoft.AspNetCore.Mvc;

namespace Jarvis.Api.Controllers;

[ApiController]
[Route("api/agent-messages")]
public class AgentMessagesController(AgentMessageRepository repo, ILogger<AgentMessagesController> logger)
    : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetRecentAsync(
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var messages = await repo.GetRecentAsync(Math.Min(limit, 200), ct);
        return Ok(messages);
    }

    [HttpGet("pending-approval")]
    public async Task<IActionResult> GetPendingApprovalAsync(CancellationToken ct = default)
    {
        var messages = await repo.GetPendingApprovalAsync(ct);
        return Ok(messages);
    }

    [HttpPost("{id:long}/approve")]
    public async Task<IActionResult> ApproveAsync(long id, CancellationToken ct = default)
    {
        var ok = await repo.ApproveAsync(id, ct);
        if (!ok) return NotFound(new { error = $"Message {id} not found or already decided." });
        logger.LogInformation("[AgentMessages] Message {Id} approved by user", id);
        return Ok(new { id, approved = true });
    }

    [HttpPost("{id:long}/deny")]
    public async Task<IActionResult> DenyAsync(long id, CancellationToken ct = default)
    {
        var ok = await repo.DenyAsync(id, ct);
        if (!ok) return NotFound(new { error = $"Message {id} not found or already decided." });
        logger.LogInformation("[AgentMessages] Message {Id} denied by user", id);
        return Ok(new { id, denied = true });
    }
}
