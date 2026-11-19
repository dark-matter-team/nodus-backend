using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nodus.Api.DTOs;
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
    public async Task<ActionResult<NodeDto>> CreateNode(Guid roadmapId, [FromBody] CreateNodeRequest request)
    {
        var (node, error) = await roadmapService.CreateNodeAsync(roadmapId, GetUserId(), request);
        if (error == "Дерево не знайдено")
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });

        return Ok(node);
    }

    [HttpPut("/api/nodes/{nodeId:guid}")]
    public async Task<ActionResult<NodeDto>> UpdateNode(Guid nodeId, [FromBody] UpdateNodeRequest request)
    {
        var (node, error) = await roadmapService.UpdateNodeAsync(nodeId, GetUserId(), request);
        if (error == "Вузол не знайдено")
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });

        return Ok(node);
    }

    [HttpPatch("/api/nodes/{nodeId:guid}/position")]
    public async Task<IActionResult> UpdateNodePosition(Guid nodeId, [FromBody] UpdatePositionRequest request)
    {
        var updated = await roadmapService.UpdateNodePositionAsync(nodeId, GetUserId(), request.PositionX, request.PositionY);
        if (!updated)
            return NotFound(new { error = "Вузол не знайдено" });

        return Ok(new { id = nodeId, positionX = request.PositionX, positionY = request.PositionY });
    }

    [HttpDelete("/api/nodes/{nodeId:guid}")]
    public async Task<IActionResult> DeleteNode(Guid nodeId)
    {
        var deleted = await roadmapService.DeleteNodeAsync(nodeId, GetUserId());
        if (!deleted)
            return NotFound(new { error = "Вузол не знайдено" });

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