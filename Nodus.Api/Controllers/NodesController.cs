using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nodus.Api.DTOs;
using Nodus.Api.Extensions;
using Nodus.Api.Services;

namespace Nodus.Api.Controllers;

[ApiController]
[Authorize]
public class NodesController(NodeService nodeService) : ControllerBase
{
    [HttpPost("/api/roadmaps/{roadmapId:guid}/nodes")]
    public async Task<ActionResult<NodeDto>> CreateNode(Guid roadmapId, [FromBody] CreateNodeRequest request)
    {
        return Ok(await nodeService.CreateNodeAsync(roadmapId, User.GetUserId(), request));
    }

    [HttpPut("/api/nodes/{nodeId:guid}")]
    public async Task<ActionResult<NodeDto>> UpdateNode(Guid nodeId, [FromBody] UpdateNodeRequest request)
    {
        return Ok(await nodeService.UpdateNodeAsync(nodeId, User.GetUserId(), request));
    }

    [HttpPatch("/api/nodes/{nodeId:guid}/position")]
    public async Task<IActionResult> UpdateNodePosition(Guid nodeId, [FromBody] UpdatePositionRequest request)
    {
        await nodeService.UpdateNodePositionAsync(nodeId, User.GetUserId(), request.PositionX, request.PositionY);
        return Ok(new { id = nodeId, positionX = request.PositionX, positionY = request.PositionY });
    }

    [HttpDelete("/api/nodes/{nodeId:guid}")]
    public async Task<IActionResult> DeleteNode(Guid nodeId)
    {
        await nodeService.DeleteNodeAsync(nodeId, User.GetUserId());
        return NoContent();
    }

    [HttpPatch("/api/checklist-items/{itemId:guid}/toggle")]
    public async Task<ActionResult<ChecklistItemDto>> ToggleChecklistItem(Guid itemId)
    {
        return Ok(await nodeService.ToggleChecklistItemAsync(itemId, User.GetUserId()));
    }

    [HttpPost("/api/nodes/{nodeId:guid}/complete")]
    public async Task<ActionResult<CompleteNodeResponse>> CompleteNode(Guid nodeId)
    {
        return Ok(await nodeService.CompleteNodeAsync(nodeId, User.GetUserId()));
    }
}
