using Jarvis.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Jarvis.Api.Controllers;

[ApiController]
[Route("api/routing-rules")]
public class RoutingRulesController(RoutingRulesService rulesService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<RoutingRuleDto>>> GetAll()
        => Ok(await rulesService.GetAllAsync());

    [HttpPost]
    public async Task<ActionResult<RoutingRuleDto>> Create([FromBody] CreateRoutingRuleRequest req)
        => Ok(await rulesService.CreateAsync(req));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateRoutingRuleRequest req)
    {
        var updated = await rulesService.UpdateAsync(id, req);
        return updated ? Ok(new { status = "updated" }) : NotFound();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await rulesService.DeleteAsync(id);
        return deleted ? Ok(new { status = "deleted" }) : NotFound();
    }

    [HttpPost("cache/invalidate")]
    public IActionResult InvalidateCache()
    {
        rulesService.InvalidateCache();
        return Ok(new { status = "cache invalidated" });
    }
}
