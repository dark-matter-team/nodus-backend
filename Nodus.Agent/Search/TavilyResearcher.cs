using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nodus.Agent.Models;
using Nodus.Agent.Pipeline;

namespace Nodus.Agent.Search;

/// <summary>
/// Step 1 without a paid model: a few searches through the free Tavily API (1,000 searches/month).
/// No LLM call here at all — Tavily returns a short answer per query plus the found pages,
/// which become the research notes and the source list. That saves the free model's daily quota.
///
/// If Tavily fails (bad key, quota used up, network), the agent continues without search
/// instead of failing the whole roadmap.
/// </summary>
public sealed class TavilyResearcher(
    IHttpClientFactory httpFactory,
    IOptions<AgentOptions> options,
    TimeProvider time,
    ILogger<TavilyResearcher> logger) : IResearcher
{
    public const string HttpClientName = "tavily";

    public sealed record Query(string Text, string? TimeRange);

    public static IReadOnlyList<Query> BuildQueries(ResearchRequest request, int year)
    {
        var goal = request.Goal.Trim();
        return
        [
            new($"{goal} learning roadmap {year}", "year"),
            new($"skills employers require for {goal} in {year}", "year"),
            new(request.FreeResourcesOnly
                ? $"best free courses and tutorials for {goal}"
                : $"best courses and books for {goal}", null),
            new($"{goal} portfolio projects for beginners", null),
        ];
    }

    public async Task<ResearchResult> ResearchAsync(ResearchRequest request, CancellationToken ct)
    {
        var queries = BuildQueries(request, time.GetUtcNow().Year);
        var responses = await Task.WhenAll(queries.Select(q => SearchAsync(q, ct)));
        var succeeded = queries.Zip(responses).Where(p => p.Second is not null).ToList();

        if (succeeded.Count == 0)
        {
            logger.LogWarning("All Tavily searches failed; continuing without web search");
            return NoSearchResearcher.CreateResult(time);
        }

        var notes = new StringBuilder();
        var sources = new List<SourceLink>();
        foreach (var (query, response) in succeeded)
        {
            notes.AppendLine($"### Search: {query.Text}");
            if (!string.IsNullOrWhiteSpace(response!.Answer))
            {
                notes.AppendLine($"Summary: {response.Answer}");
            }

            foreach (var r in response.Results)
            {
                notes.AppendLine($"- {r.Title} ({r.Url}): {Trim(r.Content, 400)}");
                sources.Add(new SourceLink(r.Title, r.Url));
            }

            notes.AppendLine();
        }

        var distinct = sources
            .Where(s => Uri.TryCreate(s.Url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https")
            .DistinctBy(s => s.Url)
            .Take(40)
            .ToList();

        logger.LogInformation("Tavily research '{Goal}': {Queries} searches, {Sources} sources",
            request.Goal, succeeded.Count, distinct.Count);

        return new ResearchResult(notes.ToString(), distinct, new UsageReport(0, 0, 0, succeeded.Count), time.GetUtcNow());
    }

    private async Task<TavilyResponse?> SearchAsync(Query query, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["query"] = query.Text,
            ["search_depth"] = "basic",
            ["max_results"] = options.Value.Tavily.MaxResultsPerQuery,
            ["include_answer"] = "basic",
        };
        if (query.TimeRange is not null)
        {
            body["time_range"] = query.TimeRange;
        }

        try
        {
            var http = httpFactory.CreateClient(HttpClientName);
            using var response = await http.PostAsync("search", JsonContent.Create(body), ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Tavily returned {Status} for '{Query}'", (int)response.StatusCode, query.Text);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<TavilyResponse>(ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                  && !ct.IsCancellationRequested)
        {
            logger.LogWarning(e, "Tavily search failed for '{Query}'", query.Text);
            return null;
        }
    }

    private static string Trim(string? text, int max)
    {
        var t = (text ?? "").ReplaceLineEndings(" ").Trim();
        return t.Length <= max ? t : t[..max] + "…";
    }

    private sealed record TavilyResponse
    {
        [JsonPropertyName("answer")] public string? Answer { get; init; }
        [JsonPropertyName("results")] public List<TavilyResult> Results { get; init; } = [];
    }

    private sealed record TavilyResult
    {
        [JsonPropertyName("title")] public string Title { get; init; } = "";
        [JsonPropertyName("url")] public string Url { get; init; } = "";
        [JsonPropertyName("content")] public string? Content { get; init; }
    }
}

/// <summary>
/// Research without web search: used when no Tavily key is configured or search fails.
/// The roadmap is then built from the model's own knowledge.
/// </summary>
public sealed class NoSearchResearcher(TimeProvider time) : IResearcher
{
    public Task<ResearchResult> ResearchAsync(ResearchRequest request, CancellationToken ct) =>
        Task.FromResult(CreateResult(time));

    public static ResearchResult CreateResult(TimeProvider time) => new(
        Notes: """
            Web search is not available, so there are no fresh search results.
            Use your own knowledge: prefer stable fundamentals over trendy tools,
            and link only to official documentation that you are confident exists.
            """,
        Sources: [],
        Usage: UsageReport.Empty,
        CreatedAt: time.GetUtcNow());
}
