using Microsoft.EntityFrameworkCore;
using Nodus.Api.Data;
using Nodus.Api.DTOs;
using Nodus.Api.Entities;
using Nodus.Api.Enums;
using Nodus.Api.Exceptions;

namespace Nodus.Api.Services;

public class NodeService(AppDbContext dbContext)
{
    private const int DefaultEstimatedMinutes = 30;

    public async Task<ChecklistItemDto> ToggleChecklistItemAsync(Guid itemId, Guid userId)
    {
        // Filtering by navigation properties does not require loading them.
        var item = await dbContext.NodeChecklistItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.Node.Roadmap.OwnerId == userId)
            ?? throw new NotFoundException("Checklist item not found");

        item.IsCompleted = !item.IsCompleted;
        await dbContext.SaveChangesAsync();

        return item.ToDto();
    }

    public async Task<CompleteNodeResponse> CompleteNodeAsync(Guid nodeId, Guid userId)
    {
        var node = await dbContext.RoadmapNodes
            .Include(n => n.ChecklistItems)
            .Include(n => n.Roadmap)
            .FirstOrDefaultAsync(n => n.Id == nodeId && n.Roadmap.OwnerId == userId)
            ?? throw new NotFoundException("Node not found");

        // Idempotent: a repeated request (e.g. a double tap) does not fail.
        if (node.Status == NodeStatus.Completed)
            return new CompleteNodeResponse(nodeId, [], node.Roadmap.Status == RoadmapStatus.Completed);

        if (node.Status == NodeStatus.Locked)
            throw new ValidationException("Cannot complete a locked node. Complete the previous step first");

        if (node.ChecklistItems.Any(i => !i.IsCompleted))
            throw new ValidationException("Complete all checklist items of this node first");

        node.Status = NodeStatus.Completed;
        node.CompletedAt = DateTime.UtcNow;

        var lockedChildren = await dbContext.RoadmapNodes
            .Where(n => n.ParentNodeId == nodeId && n.Status == NodeStatus.Locked)
            .ToListAsync();

        foreach (var child in lockedChildren)
            child.Status = NodeStatus.Available;

        var hasUnfinishedNodes = await dbContext.RoadmapNodes
            .AnyAsync(n => n.RoadmapId == node.RoadmapId && n.Id != nodeId && n.Status != NodeStatus.Completed);

        if (!hasUnfinishedNodes)
            node.Roadmap.Status = RoadmapStatus.Completed;

        await dbContext.SaveChangesAsync();

        return new CompleteNodeResponse(nodeId, lockedChildren.Select(c => c.Id).ToList(), !hasUnfinishedNodes);
    }

    public async Task<NodeDto> CreateNodeAsync(Guid roadmapId, Guid userId, CreateNodeRequest request)
    {
        var title = ValidateTitle(request.Title, "Node title");
        var checklistTitles = (request.Checklist ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => ValidateTitle(t, "Checklist item title"))
            .ToList();

        var roadmap = await dbContext.Roadmaps
            .FirstOrDefaultAsync(r => r.Id == roadmapId && r.OwnerId == userId)
            ?? throw new NotFoundException("Roadmap not found");

        var status = NodeStatus.Available;
        if (request.ParentNodeId.HasValue)
        {
            var parentStatus = await dbContext.RoadmapNodes
                .Where(n => n.Id == request.ParentNodeId.Value && n.RoadmapId == roadmapId)
                .Select(n => (NodeStatus?)n.Status)
                .FirstOrDefaultAsync()
                ?? throw new ValidationException("Parent node not found in this roadmap");

            status = parentStatus == NodeStatus.Completed ? NodeStatus.Available : NodeStatus.Locked;
        }

        var node = new RoadmapNode
        {
            RoadmapId = roadmapId,
            ParentNodeId = request.ParentNodeId,
            Title = title,
            Description = request.Description?.Trim() ?? string.Empty,
            Category = request.Category,
            EstimatedMinutes = request.EstimatedMinutes > 0 ? request.EstimatedMinutes : DefaultEstimatedMinutes,
            Status = status,
            PositionX = request.PositionX,
            PositionY = request.PositionY,
            ChecklistItems = checklistTitles
                .Select((t, index) => new NodeChecklistItem { Title = t, SortOrder = index + 1 })
                .ToList()
        };

        // A finished roadmap with a new unfinished node is active again.
        if (roadmap.Status == RoadmapStatus.Completed)
            roadmap.Status = RoadmapStatus.Active;

        dbContext.RoadmapNodes.Add(node);
        await dbContext.SaveChangesAsync();

        return node.ToDto();
    }

    public async Task<NodeDto> UpdateNodeAsync(Guid nodeId, Guid userId, UpdateNodeRequest request)
    {
        var title = ValidateTitle(request.Title, "Node title");

        var node = await dbContext.RoadmapNodes
            .Include(n => n.ChecklistItems)
            .FirstOrDefaultAsync(n => n.Id == nodeId && n.Roadmap.OwnerId == userId)
            ?? throw new NotFoundException("Node not found");

        if (request.ParentNodeId != node.ParentNodeId)
            await ChangeParentAsync(node, request.ParentNodeId);

        node.Title = title;
        node.Description = request.Description?.Trim() ?? string.Empty;
        node.Category = request.Category;
        node.EstimatedMinutes = request.EstimatedMinutes > 0 ? request.EstimatedMinutes : DefaultEstimatedMinutes;

        MergeChecklist(node, request.Checklist ?? []);

        await dbContext.SaveChangesAsync();

        return node.ToDto();
    }

    public async Task UpdateNodePositionAsync(Guid nodeId, Guid userId, double positionX, double positionY)
    {
        // Called on every drag: a single UPDATE without loading the entity.
        var updatedRows = await dbContext.RoadmapNodes
            .Where(n => n.Id == nodeId && n.Roadmap.OwnerId == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.PositionX, positionX)
                .SetProperty(n => n.PositionY, positionY));

        if (updatedRows == 0)
            throw new NotFoundException("Node not found");
    }

    public async Task DeleteNodeAsync(Guid nodeId, Guid userId)
    {
        var node = await dbContext.RoadmapNodes
            .Include(n => n.Roadmap)
            .FirstOrDefaultAsync(n => n.Id == nodeId && n.Roadmap.OwnerId == userId)
            ?? throw new NotFoundException("Node not found");

        var newParentCompleted = node.ParentNodeId is null ||
            await dbContext.RoadmapNodes.AnyAsync(n => n.Id == node.ParentNodeId && n.Status == NodeStatus.Completed);

        // Children are re-linked to the deleted node's parent ("grandparent").
        var children = await dbContext.RoadmapNodes
            .Where(n => n.ParentNodeId == nodeId)
            .ToListAsync();

        foreach (var child in children)
        {
            child.ParentNodeId = node.ParentNodeId;
            if (newParentCompleted && child.Status == NodeStatus.Locked)
                child.Status = NodeStatus.Available;
        }

        var hasOtherNodes = await dbContext.RoadmapNodes
            .AnyAsync(n => n.RoadmapId == node.RoadmapId && n.Id != nodeId);
        var hasUnfinishedNodes = await dbContext.RoadmapNodes
            .AnyAsync(n => n.RoadmapId == node.RoadmapId && n.Id != nodeId && n.Status != NodeStatus.Completed);

        if (hasOtherNodes && !hasUnfinishedNodes)
            node.Roadmap.Status = RoadmapStatus.Completed;

        dbContext.RoadmapNodes.Remove(node);

        // One SaveChanges = one transaction: re-linking and deletion succeed or fail together.
        await dbContext.SaveChangesAsync();
    }

    private async Task ChangeParentAsync(RoadmapNode node, Guid? newParentId)
    {
        if (newParentId is null)
        {
            if (node.Status == NodeStatus.Locked)
                node.Status = NodeStatus.Available;

            node.ParentNodeId = null;
            return;
        }

        if (newParentId == node.Id)
            throw new ValidationException("A node cannot be its own parent");

        var links = await dbContext.RoadmapNodes
            .Where(n => n.RoadmapId == node.RoadmapId)
            .Select(n => new { n.Id, n.ParentNodeId, n.Status })
            .ToDictionaryAsync(n => n.Id);

        if (!links.TryGetValue(newParentId.Value, out var newParent))
            throw new ValidationException("Parent node not found in this roadmap");

        // Cycle protection: walk up from the new parent to the root.
        Guid? currentId = newParentId;
        while (currentId.HasValue)
        {
            if (currentId.Value == node.Id)
                throw new ValidationException("A descendant node cannot become the parent (this would create a cycle)");

            currentId = links.TryGetValue(currentId.Value, out var current) ? current.ParentNodeId : null;
        }

        // A completed node stays completed; otherwise availability follows the new parent.
        if (node.Status != NodeStatus.Completed)
            node.Status = newParent.Status == NodeStatus.Completed ? NodeStatus.Available : NodeStatus.Locked;

        node.ParentNodeId = newParentId;
    }

    /// <summary>
    /// Updates items that keep their id, inserts new ones (empty or unknown id), deletes missing ones.
    /// Item ids stay stable, so clients can keep using them for the toggle endpoint.
    /// </summary>
    private void MergeChecklist(RoadmapNode node, List<ChecklistItemDto> incoming)
    {
        var existing = node.ChecklistItems.ToDictionary(i => i.Id);
        var keptIds = new HashSet<Guid>();
        var sortOrder = 1;

        foreach (var dto in incoming.Where(c => !string.IsNullOrWhiteSpace(c.Title)))
        {
            var itemTitle = ValidateTitle(dto.Title, "Checklist item title");

            if (existing.TryGetValue(dto.Id, out var item) && keptIds.Add(dto.Id))
            {
                item.Title = itemTitle;
                item.IsCompleted = dto.IsCompleted;
                item.SortOrder = sortOrder++;
                continue;
            }

            // Explicit Add: an entity with a preset Guid key found only via navigation would be treated as existing.
            dbContext.NodeChecklistItems.Add(new NodeChecklistItem
            {
                NodeId = node.Id,
                Title = itemTitle,
                IsCompleted = dto.IsCompleted,
                SortOrder = sortOrder++
            });
        }

        foreach (var removed in existing.Values.Where(i => !keptIds.Contains(i.Id)))
        {
            node.ChecklistItems.Remove(removed);
            dbContext.NodeChecklistItems.Remove(removed);
        }
    }

    private static string ValidateTitle(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException($"{fieldName} cannot be empty");

        var trimmed = value.Trim();
        if (trimmed.Length > FieldLimits.Title)
            throw new ValidationException($"{fieldName} cannot exceed {FieldLimits.Title} characters");

        return trimmed;
    }
}
