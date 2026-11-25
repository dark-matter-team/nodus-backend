using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nodus.Agent.Models;
using Nodus.Agent.OpenAICompatible;
using Nodus.Agent.Pipeline;
using Nodus.Agent.Search;

namespace Nodus.Agent.Tests;

/// <summary>
/// The free setup (OpenAI-compatible model + Tavily search) tested without network:
/// HTTP responses come from a stub, shaped like the real APIs.
/// </summary>
public class FreeProviderTests
{
    private const string ValidDraft = """
        {
          "summary": "A short path.",
          "tags": ["data engineering"],
          "nodes": [
            { "id": "python-basics", "title": "Python basics", "description": "d", "status": "refresh",
              "category": "theory", "steps": ["s"],
              "prerequisites": [], "estimated_hours": 20, "why": "w", "resources": [] },
            { "id": "sql-basics", "title": "SQL basics", "description": "d", "status": "known",
              "category": "practice", "steps": ["s"],
              "prerequisites": [], "estimated_hours": 0, "why": "w", "resources": [] },
            { "id": "etl", "title": "ETL pipelines", "description": "d", "status": "todo",
              "category": "project", "steps": ["s"],
              "prerequisites": ["python-basics", "sql-basics"], "estimated_hours": 30, "why": "w",
              "resources": [{ "title": "Docs", "url": "https://example.com/etl", "type": "docs", "free": true }] }
          ]
        }
        """;

    private static readonly ResearchResult Research =
        new("notes", [new SourceLink("Docs", "https://example.com/etl")], UsageReport.Empty, DateTimeOffset.UnixEpoch);

    private static readonly UserProfile Profile = new() { Goal = "Data Engineer", Skills = [new SkillLevel("SQL", 3)] };

    [Fact]
    public async Task Builder_sends_schema_and_key_and_parses_answer_in_code_fences()
    {
        var llm = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.OK, Completion($"```json\n{ValidDraft}\n```")));
        using var sp = Build(llm);

        var result = await sp.GetRequiredService<IRoadmapBuilder>().BuildAsync(new BuildRequest(Profile, Research), default);

        Assert.Equal(3, result.Draft.Nodes.Count);
        Assert.Equal(new UsageReport(100, 50, 0, 0), result.Usage);

        var call = Assert.Single(llm.Calls);
        Assert.EndsWith("/v1beta/openai/chat/completions", call.Uri);
        Assert.Equal("Bearer test-key", call.Authorization);
        using var body = JsonDocument.Parse(call.Body);
        Assert.Equal("gemini-test", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("json_schema", body.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.DoesNotContain("\"format\"", call.Body); // portable schema
    }

    [Fact]
    public async Task Builder_falls_back_to_json_object_when_schema_is_rejected()
    {
        var llm = new StubHandler((_, body) => body.Contains("json_schema")
            ? StubHandler.Json(HttpStatusCode.BadRequest, """{"error":{"message":"response_format json_schema is not supported"}}""")
            : StubHandler.Json(HttpStatusCode.OK, Completion(ValidDraft)));
        using var sp = Build(llm);

        var result = await sp.GetRequiredService<IRoadmapBuilder>().BuildAsync(new BuildRequest(Profile, Research), default);

        Assert.Equal(3, result.Draft.Nodes.Count);
        Assert.Equal(2, llm.Calls.Count);
        using var second = JsonDocument.Parse(llm.Calls[1].Body);
        Assert.Equal("json_object", second.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Contains("JSON Schema", second.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Builder_retries_after_rate_limit()
    {
        var first = true;
        var llm = new StubHandler((_, _) =>
        {
            if (!first)
            {
                return StubHandler.Json(HttpStatusCode.OK, Completion(ValidDraft));
            }

            first = false;
            var limited = StubHandler.Json(HttpStatusCode.TooManyRequests, """{"error":{"message":"rate limit"}}""");
            limited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return limited;
        });
        using var sp = Build(llm);

        var result = await sp.GetRequiredService<IRoadmapBuilder>().BuildAsync(new BuildRequest(Profile, Research), default);

        Assert.Equal(3, result.Draft.Nodes.Count);
        Assert.Equal(2, llm.Calls.Count);
    }

    [Fact]
    public async Task Busy_model_switches_to_fallback_and_stays_there()
    {
        var llm = new StubHandler((_, body) => body.Contains("\"gemini-test\"")
            ? Busy()
            : StubHandler.Json(HttpStatusCode.OK, Completion(ValidDraft)));
        using var sp = Build(llm, fallbacks: ["gemini-backup"]);
        var builder = sp.GetRequiredService<IRoadmapBuilder>();

        var result = await builder.BuildAsync(new BuildRequest(Profile, Research), default);
        Assert.Equal(3, result.Draft.Nodes.Count);
        // main model: first try + one retry, then the fallback
        Assert.Equal(["gemini-test", "gemini-test", "gemini-backup"], llm.Calls.Select(ModelOf));

        llm.Calls.Clear();
        await builder.BuildAsync(new BuildRequest(Profile, Research), default);
        Assert.Equal(["gemini-backup"], llm.Calls.Select(ModelOf)); // the fallback that worked goes first
    }

    [Fact]
    public async Task Too_slow_model_switches_to_fallback()
    {
        var llm = new StubHandler(
            (_, _) => StubHandler.Json(HttpStatusCode.OK, Completion(ValidDraft)),
            hangWhen: body => body.Contains("\"gemini-test\""));
        using var sp = Build(llm, fallbacks: ["gemini-backup"], fallbackAfterSeconds: 1);

        var result = await sp.GetRequiredService<IRoadmapBuilder>().BuildAsync(new BuildRequest(Profile, Research), default);

        Assert.Equal(3, result.Draft.Nodes.Count);
        Assert.Equal("gemini-backup", result.Model);
        Assert.Equal(["gemini-test", "gemini-backup"], llm.Calls.Select(ModelOf));
    }

    [Fact]
    public async Task All_models_busy_gives_a_clear_error()
    {
        var llm = new StubHandler((_, _) => Busy());
        using var sp = Build(llm, fallbacks: ["gemini-backup"]);

        var e = await Assert.ThrowsAsync<RoadmapGenerationException>(() =>
            sp.GetRequiredService<IRoadmapBuilder>().BuildAsync(new BuildRequest(Profile, Research), default));
        Assert.Contains("overloaded", e.Message);
        Assert.Equal(2 + 4, llm.Calls.Count); // main: 1 + 1 retry; last fallback: 1 + 3 retries
    }

    [Fact]
    public async Task Builder_retries_once_when_answer_is_not_json()
    {
        var answers = new Queue<string>(["Sure! Here is your roadmap.", ValidDraft]);
        var llm = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.OK, Completion(answers.Dequeue())));
        using var sp = Build(llm);

        var result = await sp.GetRequiredService<IRoadmapBuilder>().BuildAsync(new BuildRequest(Profile, Research), default);

        Assert.Equal(3, result.Draft.Nodes.Count);
        Assert.Contains("not valid JSON", llm.Calls[1].Body);
    }

    [Fact]
    public async Task Unknown_model_gives_a_clear_error()
    {
        var llm = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.NotFound, """{"error":{"message":"model not found"}}"""));
        using var sp = Build(llm);

        var e = await Assert.ThrowsAsync<RoadmapGenerationException>(() =>
            sp.GetRequiredService<IRoadmapBuilder>().BuildAsync(new BuildRequest(Profile, Research), default));
        Assert.Contains("gemini-test", e.Message);
    }

    [Fact]
    public async Task Missing_key_gives_setup_hint_without_calling_the_provider()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")))
        {
            return; // a real key in the environment would be picked up — nothing to test
        }

        var llm = Ok();
        using var sp = Build(llm, apiKey: null);

        var e = await Assert.ThrowsAsync<RoadmapGenerationException>(() =>
            sp.GetRequiredService<IRoadmapBuilder>().BuildAsync(new BuildRequest(Profile, Research), default));
        Assert.Contains("Free setup", e.Message);
        Assert.Empty(llm.Calls);
    }

    [Fact]
    public async Task Tavily_research_builds_notes_and_deduplicated_sources()
    {
        var tavily = new StubHandler((_, body) => StubHandler.Json(HttpStatusCode.OK, $$"""
            {
              "query": "q",
              "answer": "Python and SQL are the basics.",
              "results": [
                { "title": "Shared page", "url": "https://example.com/shared", "content": "same in every search", "score": 0.9 },
                { "title": "Unique", "url": "https://example.com/{{body.Length}}", "content": "unique", "score": 0.5 }
              ],
              "images": [],
              "response_time": 0.5
            }
            """));
        using var sp = Build(Ok(), tavily);

        var research = await sp.GetRequiredService<IResearcher>()
            .ResearchAsync(new ResearchRequest("Data Engineer", "en", false), default);

        Assert.IsType<TavilyResearcher>(sp.GetRequiredService<IResearcher>());
        Assert.Equal(4, tavily.Calls.Count);
        Assert.All(tavily.Calls, c => Assert.Equal("Bearer tvly-test", c.Authorization));
        Assert.Equal(4, research.Usage.WebSearches);
        Assert.Contains("Summary: Python and SQL are the basics.", research.Notes);
        Assert.Single(research.Sources, s => s.Url == "https://example.com/shared");
        Assert.Contains(tavily.Calls, c => c.Body.Contains("\"time_range\":\"year\""));
    }

    [Fact]
    public async Task Tavily_failure_falls_back_to_research_without_search()
    {
        var tavily = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.Unauthorized, """{"detail":{"error":"Unauthorized"}}"""));
        using var sp = Build(Ok(), tavily);

        var research = await sp.GetRequiredService<IResearcher>()
            .ResearchAsync(new ResearchRequest("Data Engineer", "en", false), default);

        Assert.Empty(research.Sources);
        Assert.Contains("Web search is not available", research.Notes);
    }

    [Fact]
    public async Task Full_free_pipeline_runs_end_to_end()
    {
        var tavily = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.OK, """
            { "query": "q", "answer": "a", "results": [ { "title": "ETL docs", "url": "https://example.com/etl", "content": "c" } ] }
            """));
        using var sp = Build(Ok(), tavily);

        var result = await sp.GetRequiredService<RoadmapGenerator>().GenerateAsync(Profile);

        Assert.Equal(3, result.Roadmap.Nodes.Count);
        Assert.Equal("https://example.com/etl", Assert.Single(result.Roadmap.Sources).Url);
        Assert.Equal(4, result.Usage.WebSearches);
        Assert.Empty(result.Warnings);
    }

    private static StubHandler Ok() =>
        new((_, _) => StubHandler.Json(HttpStatusCode.OK, Completion(ValidDraft)));

    private static HttpResponseMessage Busy()
    {
        var busy = StubHandler.Json(HttpStatusCode.ServiceUnavailable, """{"error":{"code":503,"message":"high demand"}}""");
        busy.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
        return busy;
    }

    private static string? ModelOf(StubHandler.Call call)
    {
        using var body = JsonDocument.Parse(call.Body);
        return body.RootElement.GetProperty("model").GetString();
    }

    private static ServiceProvider Build(
        StubHandler llm, StubHandler? tavily = null, string? apiKey = "test-key", string[]? fallbacks = null,
        int? fallbackAfterSeconds = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Agent:Provider"] = "OpenAICompatible",
            ["Agent:CheckLinks"] = "false",
            ["Agent:OpenAICompatible:Model"] = "gemini-test",
            ["Agent:OpenAICompatible:ApiKey"] = apiKey,
        };
        for (var i = 0; i < (fallbacks?.Length ?? 0); i++)
        {
            settings[$"Agent:OpenAICompatible:FallbackModels:{i}"] = fallbacks![i];
        }
        if (fallbackAfterSeconds is { } seconds)
        {
            settings["Agent:OpenAICompatible:FallbackAfterSeconds"] = seconds.ToString();
        }
        if (tavily is not null)
        {
            settings["Agent:Tavily:ApiKey"] = "tvly-test";
        }

        var services = new ServiceCollection()
            .AddLogging()
            .AddRoadmapAgent(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        services.AddHttpClient(OpenAICompatibleChatClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => llm);
        if (tavily is not null)
        {
            services.AddHttpClient(TavilyResearcher.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => tavily);
        }

        return services.BuildServiceProvider();
    }

    private static string Completion(string content, string finish = "stop") => JsonSerializer.Serialize(new
    {
        choices = new[] { new { index = 0, message = new { role = "assistant", content }, finish_reason = finish } },
        usage = new { prompt_tokens = 100, completion_tokens = 50, total_tokens = 150 },
    });
}

/// <summary>Fake HTTP server: records every request and answers with the given function.</summary>
internal sealed class StubHandler(
    Func<HttpRequestMessage, string, HttpResponseMessage> respond,
    Func<string, bool>? hangWhen = null) : HttpMessageHandler
{
    private readonly Lock _lock = new();

    public List<Call> Calls { get; } = [];

    public sealed record Call(string Uri, string? Authorization, string Body);

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        if (hangWhen?.Invoke(body) == true)
        {
            lock (_lock)
            {
                Calls.Add(new Call(request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));
            }

            await Task.Delay(Timeout.Infinite, ct); // a model that accepted the request and never answers
        }

        lock (_lock)
        {
            Calls.Add(new Call(request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));
            return respond(request, body);
        }
    }
}
