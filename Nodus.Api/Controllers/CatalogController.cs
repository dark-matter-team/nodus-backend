using Microsoft.AspNetCore.Mvc;
using Nodus.Api.DTOs;
using Nodus.Api.Enums;

namespace Nodus.Api.Controllers;

[ApiController]
public class CatalogController : ControllerBase
{
    [HttpPost("/api/roadmaps/{id:guid}/publish")]
    public IActionResult PublishRoadmap(Guid id, [FromBody] PublishRoadmapRequest request)
    {
        return Ok(new { id, title = request.Title, isPublic = true });
    }

    [HttpGet("/api/public-roadmaps")]
    public ActionResult<List<PublicRoadmapSummaryDto>> GetPublicRoadmaps(
        [FromQuery] string? search,
        [FromQuery] string? tag,
        [FromQuery] KnowledgeLevel? level)
    {
        var list = new List<PublicRoadmapSummaryDto>
        {
            new(
                Guid.NewGuid(),
                "Python для аналізу даних з нуля",
                "Повний базовий курс із Python та Pandas",
                "NodusAuthor",
                "https://api.dicebear.com/7.x/bottts/svg?seed=author",
                level ?? KnowledgeLevel.Beginner,
                new List<string> { "Python", "AI", "Data Science" },
                10,
                42
            )
        };

        return Ok(list);
    }

    [HttpGet("/api/public-roadmaps/{id:guid}")]
    public ActionResult<PublicRoadmapFullDto> GetPublicRoadmapById(Guid id)
    {
        var mockTree = RoadmapsController.CreateMockRoadmap(id);
        var result = new PublicRoadmapFullDto(
            id,
            mockTree.Title,
            "Повний базовий курс із Python та Pandas",
            "NodusAuthor",
            "https://api.dicebear.com/7.x/bottts/svg?seed=author",
            KnowledgeLevel.Beginner,
            mockTree.Tags,
            42,
            mockTree.Nodes
        );
        return Ok(result);
    }

    [HttpPost("/api/public-roadmaps/{id:guid}/clone")]
    public ActionResult<CloneRoadmapResponse> CloneRoadmap(Guid id)
    {
        return Ok(new CloneRoadmapResponse(Guid.NewGuid()));
    }

}