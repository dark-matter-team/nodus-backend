using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nodus.Agent.Models;

namespace Nodus.Agent.Pipeline;

/// <summary>
/// The main pipeline. It does not depend on a specific model: the steps come in through interfaces,
/// so Claude can be swapped for a stub (Fake) or another provider.
///
///   1. Research (cached per goal)
///   2. Build the graph
///   3. Validate in code → if there are errors, one attempt to fix them
///   4. Check links
/// </summary>
public sealed class RoadmapGenerator(
    IResearcher researcher,
    IRoadmapBuilder builder,
    ILinkChecker linkChecker,
    IResearchCache cache,
    IOptions<AgentOptions> options,
    TimeProvider time,
    ILogger<RoadmapGenerator> logger)
{
    public async Task<GenerationResult> GenerateAsync(
        UserProfile profile, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var warnings = new List<string>();
        var usage = UsageReport.Empty;

        // 1. Research
        var researchRequest = new ResearchRequest(profile.Goal, profile.Language, profile.FreeResourcesOnly);
        if (!cache.TryGet(researchRequest.CacheKey, out var research))
        {
            progress?.Report("Researching the current job market…");
            research = await researcher.ResearchAsync(researchRequest, ct);
            usage += research.Usage;

            // Research without sources (search not configured or failed) is not cached,
            // so the next request tries the search again.
            if (research.Sources.Count > 0)
            {
                cache.Set(researchRequest.CacheKey, research);
            }
            else
            {
                warnings.Add("Built without web search: no fresh sources were available.");
            }
        }
        else
        {
            logger.LogInformation("Research for '{Goal}' taken from cache", profile.Goal);
        }

        // 2. Build
        progress?.Report("Building your personal roadmap…");
        var build = await builder.BuildAsync(new BuildRequest(profile, research), ct);
        usage += build.Usage;

        // 3. Validate, with one attempt to fix
        var problems = RoadmapValidator.Validate(build.Draft);
        if (problems.Count > 0)
        {
            logger.LogWarning("Roadmap failed validation ({Count}): {Problems}", problems.Count, problems);
            progress?.Report("Fixing problems in the roadmap…");
            build = await builder.BuildAsync(new BuildRequest(profile, research, build.Draft, problems), ct);
            usage += build.Usage;

            problems = RoadmapValidator.Validate(build.Draft);
            if (problems.Count > 0)
            {
                throw new RoadmapGenerationException(
                    "Could not build a valid roadmap: " + string.Join(" ", problems.Take(3)));
            }
        }

        var draft = build.Draft;

        // 4. Links
        if (options.Value.CheckLinks)
        {
            progress?.Report("Checking links…");
            var urls = draft.Nodes.SelectMany(n => n.Resources).Select(r => r.Url);
            var dead = await linkChecker.FindDeadLinksAsync(urls, ct);
            if (dead.Count > 0)
            {
                warnings.Add($"Removed broken links: {dead.Count}");
                draft = draft with
                {
                    Nodes = draft.Nodes
                        .Select(n => n with { Resources = n.Resources.Where(r => !dead.Contains(r.Url)).ToList() })
                        .ToList(),
                };
            }
        }

        var roadmap = new LearningRoadmap
        {
            Goal = profile.Goal,
            Summary = draft.Summary,
            Tags = draft.Tags,
            Nodes = draft.Nodes,
            Sources = research.Sources,
            GeneratedAt = time.GetUtcNow(),
        };

        // Models are bad at arithmetic, so the deadline check is done here, not in the summary.
        if (profile.DeadlineWeeks is > 0 && profile.HoursPerWeek > 0)
        {
            var budget = profile.HoursPerWeek * profile.DeadlineWeeks.Value;
            if (roadmap.TotalHours > budget)
            {
                warnings.Add(
                    $"The roadmap needs about {roadmap.TotalHours} h, but the deadline allows {budget} h " +
                    $"({profile.HoursPerWeek} h/week × {profile.DeadlineWeeks} weeks).");
            }
        }

        var o = options.Value;
        var model = build.Model ?? o.ActiveModel;
        var cost = !o.IsPaid
            ? "0 (free tier)"
            : usage.EstimateCostUsd(o.ActiveModel)?.ToString("0.000", CultureInfo.InvariantCulture) ?? "?";
        logger.LogInformation(
            "Roadmap '{Goal}' ready ({Model}): {Nodes} nodes, {Hours} h. Tokens in/out: {In}/{Out}, searches: {Searches}, cost ${Cost}",
            profile.Goal, model, roadmap.Nodes.Count, roadmap.TotalHours,
            usage.InputTokens, usage.OutputTokens, usage.WebSearches, cost);

        return new GenerationResult(roadmap, usage, warnings, model);
    }
}
