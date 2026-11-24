using System.Text.Json;
using Anthropic.Models.Beta.Messages;
using Nodus.Agent.Models;
using Nodus.Agent.Pipeline;

namespace Nodus.Agent.Claude;

/// <summary>
/// Small helpers for working with Claude API responses.
/// </summary>
internal static class ClaudeSupport
{
    public const string FallbackBeta = "server-side-fallback-2026-07-01";

    // Models that support server-side fallback on refusal (stop_reason = "refusal").
    private static readonly HashSet<string> FallbackModels =
        ["claude-opus-5-5", "claude-opus-5", "claude-sonnet-5-5", "claude-fable-5-2", "claude-fable-5-1"];

    public static bool UseFallbacks(ClaudeOptions o) => o.UseServerFallbacks && FallbackModels.Contains(o.Model);

    /// <summary>
    /// Newer web search versions support "dynamic filtering" (less noise in context, fewer tokens).
    /// For Haiku we use the basic version, which is supported everywhere.
    /// </summary>
    public static bool SupportsDynamicSearch(string model) =>
        !model.StartsWith("claude-haiku", StringComparison.Ordinal);

    public static Effort ParseEffort(string value) => value.Trim().ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "medium" => Effort.Medium,
        "high" => Effort.High,
        "xhigh" => Effort.Xhigh,
        "max" => Effort.Max,
        _ => throw new ArgumentException($"Unknown Effort: '{value}'. Allowed: low, medium, high, xhigh, max."),
    };

    public static UsageReport ToUsage(BetaUsage usage) => new(
        usage.InputTokens,
        usage.OutputTokens,
        usage.CacheReadInputTokens ?? 0,
        usage.ServerToolUse?.WebSearchRequests ?? 0);

    /// <summary>
    /// Always check stop_reason before reading the response.
    /// </summary>
    public static void EnsureUsable(BetaMessage response, string step)
    {
        if (response.StopReason == "refusal")
        {
            throw new RoadmapGenerationException(
                $"The model declined the \"{step}\" step. Try rephrasing the goal.");
        }

        if (response.StopReason == "max_tokens")
        {
            throw new RoadmapGenerationException(
                $"The response to the \"{step}\" step did not fit into the token limit.");
        }
    }

    public static string CollectText(IEnumerable<BetaContentBlock> content) =>
        string.Concat(content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text));

    /// <summary>
    /// On stop_reason = "pause_turn" the model's response must be sent back unchanged.
    /// The C# SDK has no .ToParam() method, so we convert through JSON:
    /// these blocks have the same shape in the response and in the request
    /// (the API asks for them back "exactly as received").
    /// </summary>
    public static List<BetaContentBlockParam> ToParams(IEnumerable<BetaContentBlock> content)
    {
        var json = JsonSerializer.Serialize(content.ToList());
        return JsonSerializer.Deserialize<List<BetaContentBlockParam>>(json)
               ?? throw new InvalidOperationException("Failed to convert response blocks.");
    }

    /// <summary>
    /// Collect links: first the ones the model cited (citations), then everything it found.
    /// </summary>
    public static IReadOnlyList<SourceLink> ExtractSources(IEnumerable<BetaContentBlock> content, int max = 40)
    {
        var cited = new List<SourceLink>();
        var found = new List<SourceLink>();

        foreach (var block in content)
        {
            if (block.TryPickText(out var text))
            {
                foreach (var citation in text.Citations ?? [])
                {
                    if (citation.TryPickCitationsWebSearchResultLocation(out var location))
                    {
                        cited.Add(new SourceLink(location.Title ?? location.Url, location.Url));
                    }
                }
            }
            else if (block.TryPickWebSearchToolResult(out var result) &&
                     result.Content.TryPickBetaWebSearchResultBlocks(out var results))
            {
                foreach (var r in results)
                {
                    found.Add(new SourceLink(r.Title, r.Url));
                }
            }
        }

        return cited.Concat(found)
            .Where(s => Uri.TryCreate(s.Url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https")
            .DistinctBy(s => s.Url)
            .Take(max)
            .ToList();
    }
}
