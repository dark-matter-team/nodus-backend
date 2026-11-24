using Microsoft.EntityFrameworkCore;
using Nodus.Api.Data;
using Nodus.Api.DTOs;
using Nodus.Api.Entities;
using Nodus.Api.Enums;
using Nodus.Api.Exceptions;

namespace Nodus.Api.Services;

public class RoadmapService(AppDbContext dbContext)
{
    // TODO(KNU-39): replace this placeholder tree with the AI agent's output.
    public async Task<RoadmapFullDto> GenerateAndSaveTreeAsync(Guid userId, GenerateRoadmapRequest request)
    {
        var goal = request.GoalPrompt?.Trim();
        if (string.IsNullOrWhiteSpace(goal))
            throw new ValidationException("Goal cannot be empty");
        if (goal.Length > FieldLimits.GoalPrompt)
            throw new ValidationException($"Goal cannot exceed {FieldLimits.GoalPrompt} characters");

        var timeCommitment = request.TimeCommitment?.Trim() ?? string.Empty;
        if (timeCommitment.Length > FieldLimits.TimeCommitment)
            throw new ValidationException($"Time commitment cannot exceed {FieldLimits.TimeCommitment} characters");

        var rootNode = new RoadmapNode
        {
            Title = "Step 1: Basics",
            Description = "Get familiar with the core concepts.",
            Category = NodeCategory.Theory,
            EstimatedMinutes = 60,
            Status = NodeStatus.Available,
            PositionX = 0,
            PositionY = 0,
            ChecklistItems =
            [
                new NodeChecklistItem { Title = "Learn the basic concepts", SortOrder = 1 },
                new NodeChecklistItem { Title = "Write a short summary", SortOrder = 2 }
            ]
        };

        var childNode = new RoadmapNode
        {
            ParentNode = rootNode,
            Title = "Step 2: Practice",
            Description = "Apply the knowledge in practice.",
            Category = NodeCategory.Practice,
            EstimatedMinutes = 120,
            Status = NodeStatus.Locked,
            PositionX = 0,
            PositionY = 200,
            ChecklistItems = [new NodeChecklistItem { Title = "Complete a practical task", SortOrder = 1 }]
        };

        var roadmap = new Roadmap
        {
            OwnerId = userId,
            Title = goal.Length <= FieldLimits.Title ? goal : goal[..FieldLimits.Title],
            GoalPrompt = goal,
            KnowledgeLevel = request.KnowledgeLevel,
            TimeCommitment = timeCommitment,
            Status = RoadmapStatus.Active,
            Nodes = [rootNode, childNode]
        };

        dbContext.Roadmaps.Add(roadmap);
        await dbContext.SaveChangesAsync();

        return roadmap.ToFullDto();
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

    public async Task<RoadmapFullDto> GetFullTreeAsync(Guid roadmapId, Guid userId)
    {
        var roadmap = await dbContext.Roadmaps
            .AsNoTracking()
            .Include(r => r.Nodes)
                .ThenInclude(n => n.ChecklistItems)
            .FirstOrDefaultAsync(r => r.Id == roadmapId && r.OwnerId == userId)
            ?? throw new NotFoundException("Roadmap not found");

        return roadmap.ToFullDto();
    }

    public async Task UpdateStatusAsync(Guid roadmapId, Guid userId, RoadmapStatus status)
    {
        // "Completed" is set only by completing the last node, never manually.
        if (status is not (RoadmapStatus.Active or RoadmapStatus.Archived))
            throw new ValidationException("Status can only be changed to Active or Archived");

        var updatedRows = await dbContext.Roadmaps
            .Where(r => r.Id == roadmapId && r.OwnerId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, status));

        if (updatedRows == 0)
            throw new NotFoundException("Roadmap not found");
    }

    public async Task DeleteRoadmapAsync(Guid roadmapId, Guid userId)
    {
        // Nodes and checklist items are removed by the database cascade.
        var deletedRows = await dbContext.Roadmaps
            .Where(r => r.Id == roadmapId && r.OwnerId == userId)
            .ExecuteDeleteAsync();

        if (deletedRows == 0)
            throw new NotFoundException("Roadmap not found");
    }
}
