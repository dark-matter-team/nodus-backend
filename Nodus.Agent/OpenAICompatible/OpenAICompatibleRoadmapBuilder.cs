using System.Text.Json;
using Nodus.Agent.Claude;
using Nodus.Agent.Models;
using Nodus.Agent.Pipeline;

namespace Nodus.Agent.OpenAICompatible;

/// <summary>
/// Step 2 on a free model. Same prompts and schema as the Claude version; free models are less reliable
/// at strict JSON, so the answer is cleaned up (code fences, text around it) and one retry is made
/// if it still does not parse.
/// </summary>
public sealed class OpenAICompatibleRoadmapBuilder(OpenAICompatibleChatClient chat) : IRoadmapBuilder
{
    public async Task<BuildResult> BuildAsync(BuildRequest request, CancellationToken ct)
    {
        var user = Prompts.BuildUser(request);
        var usage = UsageReport.Empty;

        for (var attempt = 1; ; attempt++)
        {
            var result = await chat.CompleteJsonAsync(Prompts.BuildSystem, user, RoadmapSchema.PortableJson, ct);
            usage += new UsageReport(result.PromptTokens, result.CompletionTokens, 0, 0);

            if (result.FinishReason == "length")
            {
                throw new RoadmapGenerationException(
                    "The roadmap did not fit into the model's output limit. Increase Agent:OpenAICompatible:MaxOutputTokens.");
            }

            if (result.FinishReason == "content_filter")
            {
                throw new RoadmapGenerationException("The model's safety filter blocked the answer. Try rephrasing the goal.");
            }

            try
            {
                var draft = JsonSerializer.Deserialize<RoadmapDraft>(JsonText.ExtractObject(result.Content), RoadmapJson.Options)
                            ?? throw new JsonException("Empty response");
                return new BuildResult(draft, usage, result.Model);
            }
            catch (JsonException e) when (attempt == 1)
            {
                user = Prompts.BuildUser(request) + $"""


                    ## Your previous answer was not valid JSON
                    Error: {e.Message}
                    Return only one JSON object that matches the schema.
                    """;
            }
            catch (JsonException e)
            {
                throw new RoadmapGenerationException("The model returned invalid roadmap JSON twice. Try another model.", e);
            }
        }
    }
}

/// <summary>Pulls the JSON object out of a model answer that may contain code fences or extra text.</summary>
public static class JsonText
{
    public static string ExtractObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new JsonException("No JSON object found in the answer.");
        }

        return text[start..(end + 1)];
    }
}
