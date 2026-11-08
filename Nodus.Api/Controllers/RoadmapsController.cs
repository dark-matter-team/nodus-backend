using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nodus.Api.DTOs;
using Nodus.Api.Enums;
using Nodus.Api.Services;

namespace Nodus.Api.Controllers;

[ApiController]
[Route("api/roadmaps")]
[Authorize]
public class RoadmapsController(RoadmapService roadmapService) : ControllerBase
{
    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.Parse(sub!);
    }

    [HttpPost("generate")]
    public async Task<ActionResult<RoadmapFullDto>> GenerateRoadmap([FromBody] GenerateRoadmapRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GoalPrompt))
            return BadRequest(new { error = "Ціль не може бути порожньою" });

        var result = await roadmapService.GenerateAndSaveTreeAsync(GetUserId(), request);
        return Ok(result);
    }

    [HttpGet]
    public async Task<ActionResult<List<RoadmapSummaryDto>>> GetUserRoadmaps()
    {
        var list = await roadmapService.GetUserRoadmapsAsync(GetUserId());
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RoadmapFullDto>> GetRoadmapById(Guid id)
    {
        var roadmap = await roadmapService.GetFullTreeAsync(id, GetUserId());
        if (roadmap is null)
            return NotFound(new { error = "Дерево не знайдено" });

        return Ok(roadmap);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateRoadmapStatus(Guid id, [FromBody] UpdateStatusRequest request)
    {
        var updated = await roadmapService.UpdateStatusAsync(id, GetUserId(), request.Status);
        if (!updated)
            return NotFound(new { error = "Дерево не знайдено" });

        return Ok(new { id, status = request.Status });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteRoadmap(Guid id)
    {
        var deleted = await roadmapService.DeleteRoadmapAsync(id, GetUserId());
        if (!deleted)
            return NotFound(new { error = "Дерево не знайдено" });

        return NoContent();
    }

    public static RoadmapFullDto CreateMockRoadmap(Guid? id = null, string title = "Python для аналізу даних")
    {
        var roadmapId = id ?? Guid.NewGuid();
        var rootNodeId = Guid.NewGuid();
        var nodes = new List<NodeDto>
        {
            new(rootNodeId, null, "Базовий синтаксис Python", "Змінні, цикли та функції.",
                NodeCategory.Theory, 120, NodeStatus.Available, 0, 0,
                [new(Guid.NewGuid(), "Встановити Python", true, 1)])
        };
        return new RoadmapFullDto(roadmapId, title, RoadmapStatus.Active, false, ["Python", "AI"], nodes);
    }
}