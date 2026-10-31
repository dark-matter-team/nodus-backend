using Nodus.Api.Enums;

namespace Nodus.Api.DTOs;

public record GenerateRoadmapRequest(
    string GoalPrompt,
    KnowledgeLevel KnowledgeLevel,
    string TimeCommitment
);

public record UpdateStatusRequest(RoadmapStatus Status);

public record RoadmapSummaryDto(
    Guid Id,
    string Title,
    RoadmapStatus Status,
    int TotalNodesCount,
    int CompletedNodesCount,
    bool IsPublic
);

public record RoadmapFullDto(
    Guid Id,
    string Title,
    RoadmapStatus Status,
    bool IsPublic,
    List<string> Tags,
    List<NodeDto> Nodes
);