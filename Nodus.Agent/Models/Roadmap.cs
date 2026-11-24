namespace Nodus.Agent.Models;

/// <summary>
/// A finished learning roadmap: a graph of nodes (skills) with dependencies.
/// This is the contract with the frontend — change it together with them.
/// </summary>
public sealed record LearningRoadmap
{
    public required string Goal { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<RoadmapNode> Nodes { get; init; }

    /// <summary>3–5 short lowercase tags for the catalog (e.g. "python", "backend").</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Sources found during the research step.</summary>
    public IReadOnlyList<SourceLink> Sources { get; init; } = [];

    public DateTimeOffset GeneratedAt { get; init; }

    /// <summary>Hours left to study (excluding topics the user already knows).</summary>
    public int TotalHours => Nodes.Where(n => n.Status != NodeStatus.Known).Sum(n => n.EstimatedHours);
}

public sealed record RoadmapNode
{
    /// <summary>Unique kebab-case id: "sql-advanced".</summary>
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required NodeStatus Status { get; init; }

    /// <summary>Ids of nodes that must be completed first.</summary>
    public IReadOnlyList<string> Prerequisites { get; init; } = [];

    public int EstimatedHours { get; init; }

    /// <summary>theory | practice | project.</summary>
    public NodeCategory Category { get; init; } = NodeCategory.Theory;

    /// <summary>3–5 concrete micro-steps: the node's checklist in the app.</summary>
    public IReadOnlyList<string> Steps { get; init; } = [];

    /// <summary>Why this matters for the goal (one sentence).</summary>
    public string Why { get; init; } = "";

    public IReadOnlyList<LearningResource> Resources { get; init; } = [];
}

public enum NodeStatus
{
    /// <summary>Already known — can be skipped.</summary>
    Known,
    /// <summary>Partly known — worth a refresher.</summary>
    Refresh,
    /// <summary>Needs to be learned.</summary>
    Todo,
}

public enum NodeCategory
{
    /// <summary>Learning concepts.</summary>
    Theory,
    /// <summary>Exercises and hands-on practice.</summary>
    Practice,
    /// <summary>Building something end to end.</summary>
    Project,
}

public sealed record LearningResource
{
    public required string Title { get; init; }
    public required string Url { get; init; }

    /// <summary>docs | course | book | video | article | practice</summary>
    public required string Type { get; init; }

    public bool Free { get; init; }
}

public sealed record SourceLink(string Title, string Url);

/// <summary>
/// What the model returns at the build step (no metadata).
/// </summary>
public sealed record RoadmapDraft
{
    public required string Summary { get; init; }
    public required IReadOnlyList<RoadmapNode> Nodes { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}
