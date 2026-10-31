namespace Nodus.Api.Entities;

public class NodeChecklistItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid NodeId { get; set; }
    public RoadmapNode Node { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public int SortOrder { get; set; }
}