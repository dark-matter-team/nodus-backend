using Anthropic;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nodus.Agent.Pipeline;

namespace Nodus.Agent.Claude;


public sealed class ClaudeResearcher(
    AnthropicClient client,
    IOptions<AgentOptions> options,
    ILogger<ClaudeResearcher> logger) : IResearcher
{
    private const int MaxContinuations = 3;

    public async Task<ResearchResult> ResearchAsync(ResearchRequest request, CancellationToken ct)
    {
        var o = options.Value.Claude;
        BetaMessageParam userMessage = new BetaUserMessageParam
        {
            Content = Prompts.ResearchUser(request, DateOnly.FromDateTime(DateTime.UtcNow)),
        };

        var assistantSoFar = new List<BetaContentBlock>();
        var usage = UsageReport.Empty;

        for (var continuation = 0; ; continuation++)
        {
            List<BetaMessageParam> messages = [userMessage];
            if (assistantSoFar.Count > 0)
            {
                messages.Add(new BetaAssistantMessageParam { Content = ClaudeSupport.ToParams(assistantSoFar) });
            }

            var response = await client.Beta.Messages.Create(CreateParams(o, messages), ct);
            usage += ClaudeSupport.ToUsage(response.Usage);
            ClaudeSupport.EnsureUsable(response, "research");
            assistantSoFar.AddRange(response.Content);

            // The API may pause a long search. Send the response back and the model continues.
            if (response.StopReason == "pause_turn" && continuation < MaxContinuations)
            {
                logger.LogInformation("Research paused (pause_turn), continuing #{N}", continuation + 1);
                continue;
            }

            break;
        }

        var notes = ClaudeSupport.CollectText(assistantSoFar);
        if (string.IsNullOrWhiteSpace(notes))
        {
            throw new RoadmapGenerationException("Could not gather information about the goal. Please try again later.");
        }

        var sources = ClaudeSupport.ExtractSources(assistantSoFar);
        logger.LogInformation(
            "Research '{Goal}': {Chars} chars of notes, {Sources} sources, {Searches} searches",
            request.Goal, notes.Length, sources.Count, usage.WebSearches);

        return new ResearchResult(notes, sources, usage, DateTimeOffset.UtcNow);
    }

    private static MessageCreateParams CreateParams(ClaudeOptions o, List<BetaMessageParam> messages)
    {
        var fallbacks = ClaudeSupport.UseFallbacks(o);

        BetaToolUnion webSearch = ClaudeSupport.SupportsDynamicSearch(o.Model)
            ? new BetaToolUnion(new BetaWebSearchTool20260318 { MaxUses = o.MaxSearches })
            : new BetaToolUnion(new BetaWebSearchTool20250305 { MaxUses = o.MaxSearches });

        return new MessageCreateParams
        {
            Model = o.Model,
            MaxTokens = 16000,
            System = Prompts.ResearchSystem,
            OutputConfig = new BetaOutputConfig { Effort = ClaudeSupport.ParseEffort(o.Effort) },
            Tools = [webSearch],
            Messages = messages,
            Betas = fallbacks ? [ClaudeSupport.FallbackBeta] : null,
            Fallbacks = fallbacks ? (BetaFallbacksParam)new Default() : null,
        };
    }
}
