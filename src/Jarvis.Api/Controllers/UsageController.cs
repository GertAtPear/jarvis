using Jarvis.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Jarvis.Api.Controllers;

[ApiController]
[Route("api/usage")]
public class UsageController(UsageAnalyticsService analytics) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<UsageSummary>> Summary(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string?   agentName)
    {
        var (f, t) = DefaultRange(from, to);
        return Ok(await analytics.GetSummaryAsync(f, t, agentName));
    }

    [HttpGet("daily")]
    public async Task<ActionResult<IEnumerable<DailyUsage>>> Daily(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to)
    {
        var (f, t) = DefaultRange(from, to);
        return Ok(await analytics.GetDailyAsync(f, t));
    }

    [HttpGet("by-agent")]
    public async Task<ActionResult<IEnumerable<AgentUsage>>> ByAgent(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to)
    {
        var (f, t) = DefaultRange(from, to);
        return Ok(await analytics.GetByAgentAsync(f, t));
    }

    [HttpGet("routing-analytics")]
    public async Task<ActionResult<IEnumerable<RoutingAnalytic>>> RoutingAnalytics(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to)
    {
        var (f, t) = DefaultRange(from, to);
        return Ok(await analytics.GetRoutingAnalyticsAsync(f, t));
    }

    [HttpGet("escalations")]
    public async Task<ActionResult<IEnumerable<EscalationRecord>>> Escalations(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int       limit = 50)
    {
        var (f, t) = DefaultRange(from, to);
        return Ok(await analytics.GetEscalationsAsync(f, t, limit));
    }

    [HttpGet("slowest")]
    public async Task<ActionResult<IEnumerable<SlowRequest>>> Slowest([FromQuery] int limit = 20)
        => Ok(await analytics.GetSlowestAsync(limit));

    [HttpGet("cost-rates")]
    public async Task<ActionResult<IEnumerable<CostRate>>> CostRates()
        => Ok(await analytics.GetCostRatesAsync());

    [HttpPost("cost-rates")]
    public async Task<IActionResult> UpsertCostRate([FromBody] UpsertCostRateRequest req)
    {
        await analytics.UpsertCostRateAsync(req.ProviderName, req.ModelId,
            req.InputCostPer1k, req.OutputCostPer1k);
        return Ok(new { status = "updated" });
    }

    private static (DateOnly From, DateOnly To) DefaultRange(DateOnly? from, DateOnly? to)
    {
        var t = to   ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var f = from ?? t.AddDays(-6);
        return (f, t);
    }
}
