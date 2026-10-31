using Nodus.Api.Enums;

namespace Nodus.Api.DTOs;

public record CreateNodeRequest(
    Guid? ParentNodeId,
    string Title,
    string Description,
    NodeCategory Category,
    int EstimatedMinutes,
    double PositionX,
    double PositionY,
    List<string> Checklist
);

public record UpdateNodeRequest(
    Guid? ParentNodeId,
    string Title,
    string Description,
    NodeCategory Category,
    int EstimatedMinutes,
    List<ChecklistItemDto> Checklist
);

public record UpdatePositionRequest(double PositionX, double PositionY);

public record ChecklistItemDto(
    Guid Id,
    string Title,
    bool IsCompleted,
    int SortOrder
);

public record NodeDto(
    Guid Id,
    Guid? ParentNodeId,
    string Title,
    string Description,
    NodeCategory Category,
    int EstimatedMinutes,
    NodeStatus Status,
    double PositionX,
    double PositionY,
    List<ChecklistItemDto> Checklist
);

public record CompleteNodeResponse(
    Guid CompletedNodeId,
    List<Guid> UnlockedNodeIds,
    bool IsRoadmapCompleted
);