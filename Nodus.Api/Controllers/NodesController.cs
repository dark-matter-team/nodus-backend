using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nodus.Api.DTOs;
using Nodus.Api.Enums;
using Nodus.Api.Services;

namespace Nodus.Api.Controllers;

[ApiController]
[Authorize]
public class NodesController(RoadmapService roadmapService) : ControllerBase
{
    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.Parse(sub!);
    }

    [HttpPost("/api/roadmaps/{roadmapId:guid}/nodes")]
    public ActionResult<NodeDto> CreateNode(Guid roadmapId, [FromBody] CreateNodeRequest request)
    {
        var checklist = request.Checklist
            .Select((title, index) => new ChecklistItemDto(Guid.NewGuid(), title, false, index + 1))
            .ToList();

        var node = new NodeDto(
            Guid.NewGuid(),
            request.ParentNodeId,
            request.Title,
            request.Description,
            request.Category,
            request.EstimatedMinutes,
            request.ParentNodeId == null ? NodeStatus.Available : NodeStatus.Locked,
            request.PositionX,
            request.PositionY,
            checklist
        );

        return Ok(node);
    }

    [HttpPut("/api/nodes/{nodeId:guid}")]
    public ActionResult<NodeDto> UpdateNode(Guid nodeId, [FromBody] UpdateNodeRequest request)
    {
        var node = new NodeDto(
            nodeId,
            request.ParentNodeId,
            request.Title,
            request.Description,
            request.Category,
            request.EstimatedMinutes,
            NodeStatus.Available,
            0,
            0,
            request.Checklist
        );

        return Ok(node);
    }

    [HttpPatch("/api/nodes/{nodeId:guid}/position")]
    public IActionResult UpdateNodePosition(Guid nodeId, [FromBody] UpdatePositionRequest request)
    {
        return Ok(new { id = nodeId, positionX = request.PositionX, positionY = request.PositionY });
    }

    [HttpDelete("/api/nodes/{nodeId:guid}")]
    public IActionResult DeleteNode(Guid nodeId)
    {
        return NoContent();
    }

    [HttpPatch("/api/checklist-items/{itemId:guid}/toggle")]
    public async Task<ActionResult<ChecklistItemDto>> ToggleChecklistItem(Guid itemId)
    {
        var result = await roadmapService.ToggleChecklistItemAsync(itemId, GetUserId());
        if (result is null)
            return NotFound(new { error = "Пункт чек-листа не знайдено" });

        return Ok(result);
    }

    [HttpPost("/api/nodes/{nodeId:guid}/complete")]
    public async Task<ActionResult<CompleteNodeResponse>> CompleteNode(Guid nodeId)
    {
        var (response, error) = await roadmapService.CompleteNodeAsync(nodeId, GetUserId());
        if (error == "Вузол не знайдено")
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });

        return Ok(response);
    }
}