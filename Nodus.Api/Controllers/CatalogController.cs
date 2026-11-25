using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nodus.Api.DTOs;
using Nodus.Api.Enums;
using Nodus.Api.Extensions;
using Nodus.Api.Services;

namespace Nodus.Api.Controllers;

[ApiController]
public class CatalogController(CatalogService catalogService) : ControllerBase
{
    [Authorize]
    [HttpPost("/api/roadmaps/{id:guid}/publish")]
    public async Task<ActionResult<PublicRoadmapSummaryDto>> PublishRoadmap(
        Guid id,
        [FromBody] PublishRoadmapRequest request)
    {
        var result = await catalogService.PublishRoadmapAsync(id, User.GetUserId(), request);
        return Ok(result);
    }

    [HttpGet("/api/public-roadmaps")]
    public async Task<ActionResult<List<PublicRoadmapSummaryDto>>> GetPublicRoadmaps(
        [FromQuery] string? search,
        [FromQuery] string? tag,
        [FromQuery] KnowledgeLevel? level,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = CatalogService.DefaultPageSize)
    {
        var list = await catalogService.GetPublicRoadmapsAsync(search, tag, level, page, pageSize);
        return Ok(list);
    }

    [HttpGet("/api/public-roadmaps/{id:guid}")]
    public async Task<ActionResult<PublicRoadmapFullDto>> GetPublicRoadmapById(Guid id)
    {
        var roadmap = await catalogService.GetPublicRoadmapByIdAsync(id);
        return Ok(roadmap);
    }

    [Authorize]
    [HttpPost("/api/public-roadmaps/{id:guid}/clone")]
    public async Task<ActionResult<CloneRoadmapResponse>> CloneRoadmap(Guid id)
    {
        var result = await catalogService.ClonePublicRoadmapAsync(id, User.GetUserId());
        return Ok(result);
    }
}