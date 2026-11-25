using Microsoft.EntityFrameworkCore;
using Nodus.Api.Data;
using Nodus.Api.DTOs;
using Nodus.Domain;
using Nodus.Domain.Entities;
using Nodus.Domain.Enums;
using Nodus.Api.Exceptions;

namespace Nodus.Api.Services;

public class CatalogService(AppDbContext dbContext)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    public async Task<PublicRoadmapSummaryDto> PublishRoadmapAsync(
        Guid roadmapId,
        Guid userId,
        PublishRoadmapRequest request)
    {
        var roadmap = await dbContext.Roadmaps
            .Include(r => r.Owner)
            .FirstOrDefaultAsync(r => r.Id == roadmapId && r.OwnerId == userId)
            ?? throw new NotFoundException("Roadmap not found");

        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ValidationException("Title cannot be empty");
        if (request.Title.Trim().Length > FieldLimits.Title)
            throw new ValidationException($"Title cannot exceed {FieldLimits.Title} characters");

        var nodesCount = await dbContext.RoadmapNodes.CountAsync(n => n.RoadmapId == roadmapId);
        if (nodesCount == 0)
            throw new ValidationException("Cannot publish an empty roadmap");

        roadmap.Title = request.Title.Trim();
        roadmap.PublicDescription = string.IsNullOrWhiteSpace(request.PublicDescription)
            ? null
            : request.PublicDescription.Trim();
        roadmap.KnowledgeLevel = request.KnowledgeLevel;
        roadmap.Tags = (request.Tags ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();
        roadmap.IsPublic = true;

        await dbContext.SaveChangesAsync();

        return new PublicRoadmapSummaryDto(
            roadmap.Id,
            roadmap.Title,
            roadmap.PublicDescription,
            roadmap.Owner.Nickname,
            roadmap.Owner.AvatarUrl,
            roadmap.KnowledgeLevel,
            roadmap.Tags,
            nodesCount,
            roadmap.ClonesCount
        );
    }

    public async Task<List<PublicRoadmapSummaryDto>> GetPublicRoadmapsAsync(
        string? search,
        string? tag,
        KnowledgeLevel? level,
        int page = 1,
        int pageSize = DefaultPageSize)
    {
        if (page < 1)
            throw new ValidationException("Page must be 1 or greater");
        if (pageSize < 1 || pageSize > MaxPageSize)
            throw new ValidationException($"Page size must be between 1 and {MaxPageSize}");

        var query = dbContext.Roadmaps
            .AsNoTracking()
            .Where(r => r.IsPublic);

        if (level.HasValue)
        {
            query = query.Where(r => r.KnowledgeLevel == level.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(r =>
                r.Title.ToLower().Contains(normalizedSearch) ||
                (r.PublicDescription != null && r.PublicDescription.ToLower().Contains(normalizedSearch)));
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var normalizedTag = tag.Trim().ToLowerInvariant();
            query = query.Where(r => r.Tags.Contains(normalizedTag));
        }

        return await query
            .OrderByDescending(r => r.ClonesCount)
            .ThenByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new PublicRoadmapSummaryDto(
                r.Id,
                r.Title,
                r.PublicDescription,
                r.Owner.Nickname,
                r.Owner.AvatarUrl,
                r.KnowledgeLevel,
                r.Tags,
                r.Nodes.Count,
                r.ClonesCount
            ))
            .ToListAsync();
    }

    public async Task<PublicRoadmapFullDto> GetPublicRoadmapByIdAsync(Guid roadmapId)
    {
        var roadmap = await dbContext.Roadmaps
            .AsNoTracking()
            .Include(r => r.Owner)
            .Include(r => r.Nodes)
                .ThenInclude(n => n.ChecklistItems)
            .FirstOrDefaultAsync(r => r.Id == roadmapId && r.IsPublic)
            ?? throw new NotFoundException("Public roadmap not found");

        return new PublicRoadmapFullDto(
            roadmap.Id,
            roadmap.Title,
            roadmap.PublicDescription,
            roadmap.Owner.Nickname,
            roadmap.Owner.AvatarUrl,
            roadmap.KnowledgeLevel,
            roadmap.Tags,
            roadmap.ClonesCount,
            roadmap.Nodes.Select(n => n.ToDto()).ToList()
        );
    }

    public async Task<CloneRoadmapResponse> ClonePublicRoadmapAsync(Guid roadmapId, Guid userId)
    {
        var original = await dbContext.Roadmaps
            .AsNoTracking()
            .Include(r => r.Nodes)
                .ThenInclude(n => n.ChecklistItems)
            .FirstOrDefaultAsync(r => r.Id == roadmapId && r.IsPublic)
            ?? throw new NotFoundException("Public roadmap not found");

        var clonedRoadmapId = Guid.NewGuid();
        var nodeIdMap = original.Nodes.ToDictionary(n => n.Id, _ => Guid.NewGuid());

        var clonedNodes = original.Nodes.Select(oldNode =>
        {
            var newNodeId = nodeIdMap[oldNode.Id];
            Guid? newParentId = oldNode.ParentNodeId.HasValue && nodeIdMap.TryGetValue(oldNode.ParentNodeId.Value, out var mappedParentId)
                ? mappedParentId
                : null;

            return new RoadmapNode
            {
                Id = newNodeId,
                RoadmapId = clonedRoadmapId,
                ParentNodeId = newParentId,
                Title = oldNode.Title,
                Description = oldNode.Description,
                Category = oldNode.Category,
                EstimatedMinutes = oldNode.EstimatedMinutes,
                Status = newParentId is null ? NodeStatus.Available : NodeStatus.Locked,
                CompletedAt = null,
                PositionX = oldNode.PositionX,
                PositionY = oldNode.PositionY,
                Resources = oldNode.Resources
                    .Select(r => new NodeResource { Title = r.Title, Url = r.Url, Type = r.Type, IsFree = r.IsFree })
                    .ToList(),
                ChecklistItems = oldNode.ChecklistItems
                    .OrderBy(c => c.SortOrder)
                    .Select(c => new NodeChecklistItem
                    {
                        Id = Guid.NewGuid(),
                        NodeId = newNodeId,
                        Title = c.Title,
                        IsCompleted = false,
                        SortOrder = c.SortOrder
                    })
                    .ToList()
            };
        }).ToList();

        var clonedRoadmap = new Roadmap
        {
            Id = clonedRoadmapId,
            OwnerId = userId,
            Title = original.Title,
            GoalPrompt = original.GoalPrompt,
            Description = original.Description,
            KnowledgeLevel = original.KnowledgeLevel,
            TimeCommitment = original.TimeCommitment,
            Status = RoadmapStatus.Active,
            IsPublic = false,
            PublicDescription = null,
            ClonesCount = 0,
            ClonedFromId = original.Id,
            Tags = [.. original.Tags],
            Nodes = clonedNodes
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        dbContext.Roadmaps.Add(clonedRoadmap);
        await dbContext.SaveChangesAsync();

        await dbContext.Roadmaps
            .Where(r => r.Id == roadmapId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ClonesCount, r => r.ClonesCount + 1));

        await transaction.CommitAsync();

        return new CloneRoadmapResponse(clonedRoadmapId);
    }
}