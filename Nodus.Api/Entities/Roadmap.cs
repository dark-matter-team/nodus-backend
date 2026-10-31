using Nodus.Api.Enums;

namespace Nodus.Api.Entities;

public class Roadmap
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OwnerId { get; set; }
    public User Owner { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string GoalPrompt { get; set; } = string.Empty;
    public KnowledgeLevel KnowledgeLevel { get; set; } = KnowledgeLevel.Beginner;
    
    public string TimeCommitment { get; set; } = string.Empty;
    public RoadmapStatus Status { get; set; } = RoadmapStatus.Active;
    public bool IsPublic { get; set; }
    public string? PublicDescription { get; set; }
    public List<string> Tags { get; set; } = new();
    public int ClonesCount { get; set; }
    public Guid? ClonedFromId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<RoadmapNode> Nodes { get; set; } = new();
}