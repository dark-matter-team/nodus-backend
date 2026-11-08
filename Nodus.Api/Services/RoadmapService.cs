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
}