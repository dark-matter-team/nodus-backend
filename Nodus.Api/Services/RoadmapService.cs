using Microsoft.EntityFrameworkCore;
using Nodus.Api.Data;
using Nodus.Api.DTOs;
using Nodus.Api.Entities;
using Nodus.Api.Enums;

namespace Nodus.Api.Services;

public class RoadmapService(AppDbContext dbContext)
{
    public async Task<RoadmapFullDto> GenerateAndSaveTreeAsync(Guid userId, GenerateRoadmapRequest request)
    {
        var roadmapId = Guid.NewGuid();
        var rootNodeId = Guid.NewGuid();
        var childNodeId = Guid.NewGuid();

        var roadmap = new Roadmap
        {
            Id = roadmapId,
            OwnerId = userId,
            Title = request.GoalPrompt,
            GoalPrompt = request.GoalPrompt,
            KnowledgeLevel = request.KnowledgeLevel,
            TimeCommitment = request.TimeCommitment,
            Status = RoadmapStatus.Active,
            Tags = ["AI", "Roadmap"],
            Nodes =
            [
                new RoadmapNode
                {
                    Id = rootNodeId,
                    RoadmapId = roadmapId,
                    ParentNodeId = null,
                    Title = "Крок 1: Основи",
                    Description = "Базове ознайомлення з темою.",
                    Category = NodeCategory.Theory,
                    EstimatedMinutes = 60,
                    Status = NodeStatus.Available,
                    PositionX = 0,
                    PositionY = 0,
                    ChecklistItems =
                    [
                        new NodeChecklistItem { Id = Guid.NewGuid(), NodeId = rootNodeId, Title = "Вивчити базові поняття", SortOrder = 1 },
                        new NodeChecklistItem { Id = Guid.NewGuid(), NodeId = rootNodeId, Title = "Зробити конспект", SortOrder = 2 }
                    ]
                },
                new RoadmapNode
                {
                    Id = childNodeId,
                    RoadmapId = roadmapId,
                    ParentNodeId = rootNodeId,
                    Title = "Крок 2: Практика",
                    Description = "Застосування знань на практиці.",
                    Category = NodeCategory.Practice,
                    EstimatedMinutes = 120,
                    Status = NodeStatus.Locked,
                    PositionX = 0,
                    PositionY = 200,
                    ChecklistItems =
                    [
                        new NodeChecklistItem { Id = Guid.NewGuid(), NodeId = childNodeId, Title = "Виконати практичне завдання", SortOrder = 1 }
                    ]
                }
            ]
        };

        dbContext.Roadmaps.Add(roadmap);
        await dbContext.SaveChangesAsync();

        return MapToFullDto(roadmap);
    }

    public async Task<List<RoadmapSummaryDto>> GetUserRoadmapsAsync(Guid userId)
    {
        return await dbContext.Roadmaps
            .AsNoTracking()
            .Where(r => r.OwnerId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new RoadmapSummaryDto(
                r.Id,
                r.Title,
                r.Status,
                r.KnowledgeLevel,
                r.TimeCommitment,
                r.Nodes.Count,
                r.Nodes.Count(n => n.Status == NodeStatus.Completed),
                r.IsPublic
            ))
            .ToListAsync();
    }

    public async Task<RoadmapFullDto?> GetFullTreeAsync(Guid roadmapId, Guid userId)
    {
        var roadmap = await dbContext.Roadmaps
            .AsNoTracking()
            .Include(r => r.Nodes)
                .ThenInclude(n => n.ChecklistItems)
            .FirstOrDefaultAsync(r => r.Id == roadmapId && r.OwnerId == userId);

        return roadmap is null ? null : MapToFullDto(roadmap);
    }

    public static RoadmapFullDto MapToFullDto(Roadmap roadmap) => new(
        roadmap.Id,
        roadmap.Title,
        roadmap.Status,
        roadmap.IsPublic,
        roadmap.Tags,
        roadmap.Nodes.Select(n => new NodeDto(
            n.Id,
            n.ParentNodeId,
            n.Title,
            n.Description,
            n.Category,
            n.EstimatedMinutes,
            n.Status,
            n.PositionX,
            n.PositionY,
            n.ChecklistItems
                .OrderBy(c => c.SortOrder)
                .Select(c => new ChecklistItemDto(c.Id, c.Title, c.IsCompleted, c.SortOrder))
                .ToList()
        )).ToList()
    );

    public async Task<bool> UpdateStatusAsync(Guid roadmapId, Guid userId, RoadmapStatus status)
    {
        var roadmap = await dbContext.Roadmaps.FirstOrDefaultAsync(r => r.Id == roadmapId && r.OwnerId == userId);
        if (roadmap is null) return false;

        roadmap.Status = status;
        await dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteRoadmapAsync(Guid roadmapId, Guid userId)
    {
        var roadmap = await dbContext.Roadmaps.FirstOrDefaultAsync(r => r.Id == roadmapId && r.OwnerId == userId);
        if (roadmap is null) return false;

        dbContext.Roadmaps.Remove(roadmap);
        await dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<ChecklistItemDto?> ToggleChecklistItemAsync(Guid itemId, Guid userId)
    {
        var item = await dbContext.NodeChecklistItems
            .Include(i => i.Node)
                .ThenInclude(n => n.Roadmap)
            .FirstOrDefaultAsync(i => i.Id == itemId && i.Node.Roadmap.OwnerId == userId);

        if (item is null) return null;

        item.IsCompleted = !item.IsCompleted;
        await dbContext.SaveChangesAsync();

        return new ChecklistItemDto(item.Id, item.Title, item.IsCompleted, item.SortOrder);
    }

    public async Task<(CompleteNodeResponse? Response, string? Error)> CompleteNodeAsync(Guid nodeId, Guid userId)
    {
        var node = await dbContext.RoadmapNodes
            .Include(n => n.ChecklistItems)
            .Include(n => n.Roadmap)
                .ThenInclude(r => r.Nodes)
            .FirstOrDefaultAsync(n => n.Id == nodeId && n.Roadmap.OwnerId == userId);

        if (node is null)
            return (null, "Вузол не знайдено");

        if (node.Status == NodeStatus.Locked)
            return (null, "Неможливо завершити заблокований вузол. Спочатку завершіть попередній крок");

        if (node.ChecklistItems.Any(i => !i.IsCompleted))
            return (null, "Спочатку виконайте всі мікро-кроки в чек-листі цього вузла");

        node.Status = NodeStatus.Completed;
        node.CompletedAt = DateTime.UtcNow;

        var unlockedNodeIds = new List<Guid>();
        foreach (var child in node.Roadmap.Nodes.Where(n => n.ParentNodeId == nodeId && n.Status == NodeStatus.Locked))
        {
            child.Status = NodeStatus.Available;
            unlockedNodeIds.Add(child.Id);
        }

        var isRoadmapCompleted = node.Roadmap.Nodes.All(n => n.Status == NodeStatus.Completed);
        if (isRoadmapCompleted)
        {
            node.Roadmap.Status = RoadmapStatus.Completed;
        }

        await dbContext.SaveChangesAsync();

        return (new CompleteNodeResponse(nodeId, unlockedNodeIds, isRoadmapCompleted), null);
    }

    public async Task<(NodeDto? Node, string? Error)> UpdateNodeAsync(Guid nodeId, Guid userId, UpdateNodeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return (null, "Назва вузла не може бути порожньою");

        var node = await dbContext.RoadmapNodes
            .Include(n => n.ChecklistItems)
            .Include(n => n.Roadmap)
                .ThenInclude(r => r.Nodes)
            .FirstOrDefaultAsync(n => n.Id == nodeId && n.Roadmap.OwnerId == userId);

        if (node is null)
            return (null, "Вузол не знайдено");

        if (request.ParentNodeId.HasValue)
        {
            if (request.ParentNodeId.Value == nodeId)
                return (null, "Вузол не може бути батьківським сам для себе");

            var parentExists = node.Roadmap.Nodes.Any(n => n.Id == request.ParentNodeId.Value);
            if (!parentExists)
                return (null, "Батьківський вузол не знайдено в цьому дереві");

            // Cycle protection: walk up from proposed parent to root
            var currentId = request.ParentNodeId;
            while (currentId.HasValue)
            {
                if (currentId.Value == nodeId)
                    return (null, "Неможливо обрати дочірній вузол як батьківський (утвориться цикл)");

                currentId = node.Roadmap.Nodes.FirstOrDefault(n => n.Id == currentId.Value)?.ParentNodeId;
            }
        }

        node.ParentNodeId = request.ParentNodeId;
        node.Title = request.Title.Trim();
        node.Description = request.Description?.Trim() ?? string.Empty;
        node.Category = request.Category;
        node.EstimatedMinutes = request.EstimatedMinutes > 0 ? request.EstimatedMinutes : 30;

        dbContext.NodeChecklistItems.RemoveRange(node.ChecklistItems);

        var updatedChecklist = (request.Checklist ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Title))
            .Select((c, index) => new NodeChecklistItem
            {
                Id = Guid.NewGuid(),
                NodeId = node.Id,
                Title = c.Title.Trim(),
                IsCompleted = c.IsCompleted,
                SortOrder = index + 1
            })
            .ToList();

        dbContext.NodeChecklistItems.AddRange(updatedChecklist);
        await dbContext.SaveChangesAsync();

        var dto = new NodeDto(
            node.Id,
            node.ParentNodeId,
            node.Title,
            node.Description,
            node.Category,
            node.EstimatedMinutes,
            node.Status,
            node.PositionX,
            node.PositionY,
            updatedChecklist.Select(c => new ChecklistItemDto(c.Id, c.Title, c.IsCompleted, c.SortOrder)).ToList()
        );

        return (dto, null);
    }

    public async Task<bool> DeleteNodeAsync(Guid nodeId, Guid userId)
    {
        var node = await dbContext.RoadmapNodes
            .Include(n => n.Roadmap)
                .ThenInclude(r => r.Nodes)
            .FirstOrDefaultAsync(n => n.Id == nodeId && n.Roadmap.OwnerId == userId);

        if (node is null) return false;

        var children = node.Roadmap.Nodes.Where(n => n.ParentNodeId == nodeId).ToList();
        var isNewParentCompleted = !node.ParentNodeId.HasValue ||
            node.Roadmap.Nodes.FirstOrDefault(n => n.Id == node.ParentNodeId.Value)?.Status == NodeStatus.Completed;

        foreach (var child in children)
        {
            child.ParentNodeId = node.ParentNodeId;
            if (isNewParentCompleted && child.Status == NodeStatus.Locked)
            {
                child.Status = NodeStatus.Available;
            }
        }

        await dbContext.SaveChangesAsync();

        dbContext.RoadmapNodes.Remove(node);
        await dbContext.SaveChangesAsync();

        return true;
    }

    public async Task<(NodeDto? Node, string? Error)> CreateNodeAsync(Guid roadmapId, Guid userId, CreateNodeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return (null, "Назва вузла не може бути порожньою");

        var roadmap = await dbContext.Roadmaps
            .Include(r => r.Nodes)
            .FirstOrDefaultAsync(r => r.Id == roadmapId && r.OwnerId == userId);

        if (roadmap is null)
            return (null, "Дерево не знайдено");

        var nodeStatus = NodeStatus.Available;
        if (request.ParentNodeId.HasValue)
        {
            var parent = roadmap.Nodes.FirstOrDefault(n => n.Id == request.ParentNodeId.Value);
            if (parent is null)
                return (null, "Батьківський вузол не знайдено в цьому дереві");

            nodeStatus = parent.Status == NodeStatus.Completed ? NodeStatus.Available : NodeStatus.Locked;
        }

        var nodeId = Guid.NewGuid();
        var checklistItems = (request.Checklist ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select((title, index) => new NodeChecklistItem
            {
                Id = Guid.NewGuid(),
                NodeId = nodeId,
                Title = title.Trim(),
                IsCompleted = false,
                SortOrder = index + 1
            })
            .ToList();

        var newNode = new RoadmapNode
        {
            Id = nodeId,
            RoadmapId = roadmapId,
            ParentNodeId = request.ParentNodeId,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            Category = request.Category,
            EstimatedMinutes = request.EstimatedMinutes > 0 ? request.EstimatedMinutes : 30,
            Status = nodeStatus,
            PositionX = request.PositionX,
            PositionY = request.PositionY,
            ChecklistItems = checklistItems
        };

        if (roadmap.Status == RoadmapStatus.Completed)
        {
            roadmap.Status = RoadmapStatus.Active;
        }

        dbContext.RoadmapNodes.Add(newNode);
        await dbContext.SaveChangesAsync();

        var dto = new NodeDto(
            newNode.Id,
            newNode.ParentNodeId,
            newNode.Title,
            newNode.Description,
            newNode.Category,
            newNode.EstimatedMinutes,
            newNode.Status,
            newNode.PositionX,
            newNode.PositionY,
            checklistItems.Select(c => new ChecklistItemDto(c.Id, c.Title, c.IsCompleted, c.SortOrder)).ToList()
        );

        return (dto, null);
    }

    public async Task<bool> UpdateNodePositionAsync(Guid nodeId, Guid userId, double positionX, double positionY)
    {
        var node = await dbContext.RoadmapNodes
            .Include(n => n.Roadmap)
            .FirstOrDefaultAsync(n => n.Id == nodeId && n.Roadmap.OwnerId == userId);

        if (node is null) return false;

        node.PositionX = positionX;
        node.PositionY = positionY;
        await dbContext.SaveChangesAsync();
        return true;
    }
}