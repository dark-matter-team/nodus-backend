using Nodus.Agent.Models;

namespace Nodus.Agent.Pipeline;

/// <summary>
/// Step 1: gather up-to-date information about the goal (job market, technologies, materials).
/// The result depends only on the goal and language, so it can be cached
/// and reused across users.
/// </summary>
public interface IResearcher
{
    Task<ResearchResult> ResearchAsync(ResearchRequest request, CancellationToken ct);
}

/// <summary>
/// Step 2: turn the research + user profile into a graph.
/// </summary>
public interface IRoadmapBuilder
{
    Task<BuildResult> BuildAsync(BuildRequest request, CancellationToken ct);
}

/// <summary>
/// Link checking: returns the links that are definitely broken.
/// </summary>
public interface ILinkChecker
{
    Task<IReadOnlySet<string>> FindDeadLinksAsync(IEnumerable<string> urls, CancellationToken ct);
}

public sealed record ResearchRequest(string Goal, string Language, bool FreeResourcesOnly)
{
    public string CacheKey => $"{Goal.Trim().ToLowerInvariant()}|{Language}|{FreeResourcesOnly}";
}

public sealed record ResearchResult(
    string Notes,
    IReadOnlyList<SourceLink> Sources,
    UsageReport Usage,
    DateTimeOffset CreatedAt);

/// <summary>
/// PreviousAttempt and Problems are set when the first attempt failed validation
/// and we ask the model to fix the errors.
/// </summary>
public sealed record BuildRequest(
    UserProfile Profile,
    ResearchResult Research,
    RoadmapDraft? PreviousAttempt = null,
    IReadOnlyList<string>? Problems = null);

/// <summary>Model — the model that actually answered (a free provider may switch to a fallback).</summary>
public sealed record BuildResult(RoadmapDraft Draft, UsageReport Usage, string? Model = null);

public sealed record GenerationResult(
    LearningRoadmap Roadmap,
    UsageReport Usage,
    IReadOnlyList<string> Warnings,
    string? Model = null);

/// <summary>
/// An error that is safe to show to the user as-is.
/// </summary>
public sealed class RoadmapGenerationException(string message, Exception? inner = null)
    : Exception(message, inner);
