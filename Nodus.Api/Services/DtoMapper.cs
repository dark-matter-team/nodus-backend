using Nodus.Api.DTOs;
using Nodus.Domain.Entities;

namespace Nodus.Api.Services;

internal static class DtoMapper
{
    public static NodeDto ToDto(this RoadmapNode node) => new(
        node.Id,
        node.ParentNodeId,
        node.Title,
        node.Description,
        node.Category,
        node.EstimatedMinutes,
        node.Status,
        node.PositionX,
        node.PositionY,
        node.ChecklistItems
            .OrderBy(c => c.SortOrder)
            .Select(c => c.ToDto())
            .ToList(),
        (node.Resources ?? [])
            .Select(r => new NodeResourceDto(r.Title, r.Url, r.Type, r.IsFree))
            .ToList());

    public static ChecklistItemDto ToDto(this NodeChecklistItem item) =>
        new(item.Id, item.Title, item.IsCompleted, item.SortOrder);

    public static RoadmapFullDto ToFullDto(this Roadmap roadmap) => new(
        roadmap.Id,
        roadmap.Title,
        roadmap.Status,
        roadmap.IsPublic,
        roadmap.Tags,
        roadmap.Nodes.Select(n => n.ToDto()).ToList(),
        roadmap.Description);
}
