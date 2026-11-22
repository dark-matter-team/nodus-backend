using Microsoft.AspNetCore.Mvc;
using Nodus.Api.DTOs;
using Nodus.Api.Enums;
using Nodus.Api.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Nodus.Api.Controllers;

[ApiController]
public class CatalogController(RoadmapService roadmapService) : ControllerBase
{
    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.Parse(sub!);
    }

    [Authorize]
    [HttpPost("/api/roadmaps/{id:guid}/publish")]
    public async Task<ActionResult<PublicRoadmapSummaryDto>> PublishRoadmap(Guid id, [FromBody] PublishRoadmapRequest request)
    {
        var (result, error, statusCode) = await roadmapService.PublishRoadmapAsync(id, GetUserId(), request);
        if (error is not null)
            return StatusCode(statusCode, new { error });

        return Ok(result);
    }

    [HttpGet("/api/public-roadmaps")]
    public async Task<ActionResult<List<PublicRoadmapSummaryDto>>> GetPublicRoadmaps(
         [FromQuery] string? search,
         [FromQuery] string? tag,
         [FromQuery] KnowledgeLevel? level)
    {
        var list = await roadmapService.GetPublicRoadmapsAsync(search, tag, level);
        return Ok(list);
    }

    [HttpGet("/api/public-roadmaps/{id:guid}")]
    public async Task<ActionResult<PublicRoadmapFullDto>> GetPublicRoadmapById(Guid id)
    {
        var roadmap = await roadmapService.GetPublicRoadmapByIdAsync(id);
        if (roadmap is null)
            return NotFound(new { error = "Публічне дерево не знайдено" });

        return Ok(roadmap);
    }

    [HttpPost("/api/public-roadmaps/{id:guid}/clone")]
    public ActionResult<CloneRoadmapResponse> CloneRoadmap(Guid id)
    {
        return Ok(new CloneRoadmapResponse(Guid.NewGuid()));
    }
}