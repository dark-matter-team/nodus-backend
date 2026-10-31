using Microsoft.AspNetCore.Mvc;
using Nodus.Api.DTOs;
using Nodus.Api.Enums;

namespace Nodus.Api.Controllers;

[ApiController]
[Route("api/roadmaps")]
public class RoadmapsController : ControllerBase
{
    public static RoadmapFullDto CreateMockRoadmap(Guid? id = null, string title = "Python для аналізу даних")
    {
        var roadmapId = id ?? Guid.NewGuid();
        var rootNodeId = Guid.NewGuid();
        var childNodeId = Guid.NewGuid();

        var nodes = new List<NodeDto>
        {
            new(rootNodeId, null, "Базовий синтаксис Python", "Змінні, цикли та функції.",
                NodeCategory.Theory, 120, NodeStatus.Available, 0, 0,
                new List<ChecklistItemDto>
                {
                    new(Guid.NewGuid(), "Встановити Python та VS Code", true, 1),
                    new(Guid.NewGuid(), "Написати 5 простих функцій", false, 2)
                }),
            new(childNodeId, rootNodeId, "Бібліотека Pandas", "Робота з таблицями CSV.",
                NodeCategory.Practice, 180, NodeStatus.Locked, 0, 200,
                new List<ChecklistItemDto>
                {
                    new(Guid.NewGuid(), "Встановити pandas", false, 1)
                })
        };

        return new RoadmapFullDto(roadmapId, title, RoadmapStatus.Active, false, new List<string> { "Python", "AI" }, nodes);
    }

    [HttpPost("generate")]
    public ActionResult<RoadmapFullDto> GenerateRoadmap([FromBody] GenerateRoadmapRequest request)
    {
        return Ok(CreateMockRoadmap(title: request.GoalPrompt));
    }

    [HttpGet]
    public ActionResult<List<RoadmapSummaryDto>> GetUserRoadmaps()
    {
        var list = new List<RoadmapSummaryDto>
        {
            new(Guid.NewGuid(), "Python для аналізу даних", RoadmapStatus.Active, 5, 1, false)
        };
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    public ActionResult<RoadmapFullDto> GetRoadmapById(Guid id)
    {
        return Ok(CreateMockRoadmap(id));
    }

    [HttpPatch("{id:guid}/status")]
    public IActionResult UpdateRoadmapStatus(Guid id, [FromBody] UpdateStatusRequest request)
    {
        return Ok(new { id, status = request.Status });
    }

    [HttpDelete("{id:guid}")]
    public IActionResult DeleteRoadmap(Guid id)
    {
        return NoContent();
    }
}