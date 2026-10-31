using Nodus.Api.Enums;

namespace Nodus.Api.DTOs;

public record PublishRoadmapRequest(
    string Title,
    string PublicDescription,
    List<string> Tags,
    KnowledgeLevel KnowledgeLevel
);

public record PublicRoadmapSummaryDto(
    Guid Id,
    string Title,
    string? PublicDescription,
    string AuthorNickname,
    string? AuthorAvatarUrl,
    KnowledgeLevel KnowledgeLevel,
    List<string> Tags,
    int NodesCount,
    int ClonesCount
);

public record PublicRoadmapFullDto(
    Guid Id,
    string Title,
    string? PublicDescription,
    string AuthorNickname,
    string? AuthorAvatarUrl,
    KnowledgeLevel KnowledgeLevel,
    List<string> Tags,
    int ClonesCount,
    List<NodeDto> Nodes
);

public record CloneRoadmapResponse(
    Guid NewRoadmapId
);