namespace Nodus.Api.Entities;

/// <summary>
/// A learning material link suggested by the AI for a node. Stored as JSON inside the node row (jsonb).
/// </summary>
public class NodeResource
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;

    /// <summary>docs | course | book | video | article | practice</summary>
    public string Type { get; set; } = string.Empty;

    public bool IsFree { get; set; }
}
