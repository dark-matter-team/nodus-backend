using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Options;
using Nodus.Agent.Models;
using Nodus.Agent.Pipeline;

namespace Nodus.Agent.Claude;

/// <summary>
/// Step 2. Profile + research → a JSON graph that strictly follows the schema (structured outputs).
/// There is no web search here: structured outputs are incompatible with the citations
/// that search returns, which is why the steps are separate.
/// </summary>
public sealed class ClaudeRoadmapBuilder(AnthropicClient client, IOptions<AgentOptions> options) : IRoadmapBuilder
{
    private static readonly Dictionary<string, JsonElement> Schema = RoadmapSchema.AsDictionary();

    public async Task<BuildResult> BuildAsync(BuildRequest request, CancellationToken ct)
    {
        var o = options.Value.Claude;
        var fallbacks = ClaudeSupport.UseFallbacks(o);

        var parameters = new MessageCreateParams
        {
            Model = o.Model,
            MaxTokens = 16000,
            System = Prompts.BuildSystem,
            OutputConfig = new BetaOutputConfig
            {
                Effort = ClaudeSupport.ParseEffort(o.Effort),
                Format = new BetaJsonOutputFormat { Schema = Schema },
            },
            Messages = [new BetaUserMessageParam { Content = Prompts.BuildUser(request) }],
            Betas = fallbacks ? [ClaudeSupport.FallbackBeta] : null,
            Fallbacks = fallbacks ? (BetaFallbacksParam)new Default() : null,
        };

        var response = await client.Beta.Messages.Create(parameters, ct);
        var usage = ClaudeSupport.ToUsage(response.Usage);
        ClaudeSupport.EnsureUsable(response, "build roadmap");

        var json = ClaudeSupport.CollectText(response.Content);
        try
        {
            var draft = JsonSerializer.Deserialize<RoadmapDraft>(json, RoadmapJson.Options)
                        ?? throw new JsonException("Empty response");
            return new BuildResult(draft, usage);
        }
        catch (JsonException e)
        {
            throw new RoadmapGenerationException("The model returned invalid roadmap JSON.", e);
        }
    }
}
