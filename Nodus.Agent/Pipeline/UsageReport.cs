namespace Nodus.Agent.Pipeline;

/// <summary>
/// How many resources a generation used. Log it — that's how you see the real cost.
/// </summary>
public sealed record UsageReport(long InputTokens, long OutputTokens, long CacheReadTokens, long WebSearches)
{
    public static readonly UsageReport Empty = new(0, 0, 0, 0);

    public static UsageReport operator +(UsageReport a, UsageReport b) => new(
        a.InputTokens + b.InputTokens,
        a.OutputTokens + b.OutputTokens,
        a.CacheReadTokens + b.CacheReadTokens,
        a.WebSearches + b.WebSearches);

    // Prices in $ per 1M tokens (input, output) as of November 2026, direct Claude API.
    // Check current prices: https://platform.claude.com/docs/en/about-claude/pricing
    private static readonly Dictionary<string, (decimal Input, decimal Output)> Prices = new()
    {
        ["claude-opus-5-5"] = (4.00m, 20.00m),
        ["claude-sonnet-5-5"] = (2.00m, 10.00m),
        ["claude-haiku-5-5"] = (0.10m, 0.50m),
    };

    private const decimal WebSearchPrice = 10m / 1000m; // $10 per 1,000 searches

    /// <summary>Rough cost estimate in $. null if the model is unknown.</summary>
    public decimal? EstimateCostUsd(string model)
    {
        if (!Prices.TryGetValue(model, out var p))
        {
            return null;
        }

        // Cache reads cost ~10% of regular input.
        return (InputTokens * p.Input + CacheReadTokens * p.Input * 0.1m + OutputTokens * p.Output) / 1_000_000m
               + WebSearches * WebSearchPrice;
    }
}
