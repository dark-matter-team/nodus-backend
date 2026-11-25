using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nodus.Api.Middleware;
using Nodus.Api.DTOs;
using Nodus.Api.Extensions;
using Nodus.Api.Services;

namespace Nodus.Api.Controllers;

[ApiController]
[Route("api/roadmaps")]
[Authorize]
public class RoadmapsController(RoadmapService roadmapService) : ControllerBase
{
    // Generation calls the AI (30-90 s, free-tier quota), so it is rate-limited per user.
    [HttpPost("generate")]
    [EnableRateLimiting(RateLimitPolicies.Generate)]
    public async Task<ActionResult<RoadmapFullDto>> GenerateRoadmap([FromBody] GenerateRoadmapRequest request)
    {
        return Ok(await roadmapService.GenerateAndSaveTreeAsync(User.GetUserId(), request, HttpContext.RequestAborted));
    }

    [HttpGet]
    public async Task<ActionResult<List<RoadmapSummaryDto>>> GetUserRoadmaps()
    {
        return Ok(await roadmapService.GetUserRoadmapsAsync(User.GetUserId()));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RoadmapFullDto>> GetRoadmapById(Guid id)
    {
        return Ok(await roadmapService.GetFullTreeAsync(id, User.GetUserId()));
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateRoadmapStatus(Guid id, [FromBody] UpdateStatusRequest request)
    {
        await roadmapService.UpdateStatusAsync(id, User.GetUserId(), request.Status);
        return Ok(new { id, status = request.Status });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteRoadmap(Guid id)
    {
        await roadmapService.DeleteRoadmapAsync(id, User.GetUserId());
        return NoContent();
    }
}
