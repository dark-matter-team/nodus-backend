using Nodus.Api.Enums;

namespace Nodus.Api.Entities;

public class RoadmapNode
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoadmapId { get; set; }
    public Roadmap Roadmap { get; set; } = null!;

    public Guid? ParentNodeId { get; set; }
    public RoadmapNode? ParentNode { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public NodeCategory Category { get; set; } = NodeCategory.Theory;
    public int EstimatedMinutes { get; set; }
    public NodeStatus Status { get; set; } = NodeStatus.Locked;
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<NodeChecklistItem> ChecklistItems { get; set; } = new();
}