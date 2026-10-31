using Microsoft.AspNetCore.Mvc;
using Nodus.Api.DTOs;
using Nodus.Api.Enums;

namespace Nodus.Api.Controllers;

[ApiController]
public class NodesController : ControllerBase
{
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
    public IActionResult ToggleChecklistItem(Guid itemId)
    {
        return Ok(new { id = itemId, isCompleted = true });
    }

    [HttpPost("/api/nodes/{nodeId:guid}/complete")]
    public ActionResult<CompleteNodeResponse> CompleteNode(Guid nodeId)
    {
        return Ok(new CompleteNodeResponse(nodeId, new List<Guid> { Guid.NewGuid() }, false));
    }
}