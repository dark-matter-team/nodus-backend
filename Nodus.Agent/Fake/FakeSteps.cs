using Nodus.Agent.Models;
using Nodus.Agent.Pipeline;

namespace Nodus.Agent.Fake;

/// <summary>
/// Research stub: no network, no cost. Lets the backend and frontend
/// work while the AI part is still in development.
/// </summary>
public sealed class FakeResearcher : IResearcher
{
    public async Task<ResearchResult> ResearchAsync(ResearchRequest request, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(300), ct); // simulate work
        return new ResearchResult(
            Notes: $"Notes on current requirements for the goal \"{request.Goal}\".",
            Sources:
            [
                new SourceLink("Pro Git book", "https://git-scm.com/book/en/v2"),
                new SourceLink("Python Tutorial", "https://docs.python.org/3/tutorial/"),
            ],
            Usage: UsageReport.Empty,
            CreatedAt: DateTimeOffset.UtcNow);
    }
}

/// <summary>
/// Build stub: always the same graph, but personalized —
/// a node becomes "known" if the profile has a level-3 skill with a matching name.
/// </summary>
public sealed class FakeRoadmapBuilder : IRoadmapBuilder
{
    public async Task<BuildResult> BuildAsync(BuildRequest request, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
        var profile = request.Profile;

        RoadmapNode Node(string id, string title, string[] pre, int hours, string skillHint, NodeCategory category = NodeCategory.Theory) =>
            new()
            {
                Id = id,
                Title = title,
                Description = $"What to learn in \"{title}\".",
                Status = StatusFor(profile, skillHint),
                Prerequisites = pre,
                EstimatedHours = StatusFor(profile, skillHint) == NodeStatus.Known ? 0 : hours,
                Why = $"Needed for the goal \"{profile.Goal}\".",
                Category = category,
                Steps = [$"Read an introduction to \"{title}\"", $"Practice \"{title}\" for an hour", "Write a short summary"],
                Resources = id == "git"
                    ? [new LearningResource { Title = "Pro Git", Url = "https://git-scm.com/book/en/v2", Type = "book", Free = true }]
                    : [],
            };

        var draft = new RoadmapDraft
        {
            Summary = $"Path to \"{profile.Goal}\" at {profile.HoursPerWeek} h/week.",
            Tags = ["demo", "programming"],
            Nodes =
            [
                Node("git", "Git and working with repositories", [], 8, "git"),
                Node("python-basics", "Python basics", ["git"], 30, "python"),
                Node("sql-basics", "SQL basics", ["git"], 20, "sql"),
                Node("core-skill", $"Core tools: {profile.Goal}", ["python-basics"], 40, "-", NodeCategory.Practice),
                Node("pet-project", "Portfolio pet project", ["core-skill"], 30, "-", NodeCategory.Project),
                Node("job-search", "Resume and interviews", ["pet-project"], 10, "-"),
            ],
        };

        return new BuildResult(draft, UsageReport.Empty);
    }

    private static NodeStatus StatusFor(UserProfile profile, string hint)
    {
        var level = profile.Skills
            .Where(s => s.Name.Contains(hint, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Level)
            .DefaultIfEmpty(0)
            .Max();

        return level switch
        {
            >= 3 => NodeStatus.Known,
            >= 1 => NodeStatus.Refresh,
            _ => NodeStatus.Todo,
        };
    }
}
