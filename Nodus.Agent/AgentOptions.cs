namespace Nodus.Agent;

/// <summary>
/// Agent settings: the "Agent" section in appsettings.json, user secrets, or environment variables
/// such as Agent__OpenAICompatible__Model=gemini-3.8-flash.
/// </summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>
    /// Which model runs the agent:
    /// "OpenAICompatible" — any OpenAI-compatible API, free tiers included (Gemini, Groq, OpenRouter, local Ollama);
    /// "Claude" — Claude API (paid, best quality, built-in web search);
    /// "Fake" — a stub with no network and no cost.
    /// </summary>
    public string Provider { get; set; } = Providers.OpenAICompatible;

    /// <summary>How many hours to keep the research for a goal.</summary>
    public int ResearchCacheHours { get; set; } = 24 * 7;

    /// <summary>Whether to check links with an HTTP request and drop dead ones.</summary>
    public bool CheckLinks { get; set; } = true;

    public OpenAICompatibleOptions OpenAICompatible { get; set; } = new();

    public TavilyOptions Tavily { get; set; } = new();

    public ClaudeOptions Claude { get; set; } = new();

    /// <summary>The model that builds the roadmap with the current provider.</summary>
    public string ActiveModel => Providers.Normalize(Provider) switch
    {
        Providers.Claude => Claude.Model,
        Providers.OpenAICompatible => OpenAICompatible.Model,
        _ => "fake",
    };

    /// <summary>Only Claude is billed per token; the others run on free tiers (or locally).</summary>
    public bool IsPaid => Providers.Normalize(Provider) == Providers.Claude;
}

public static class Providers
{
    public const string OpenAICompatible = "OpenAICompatible";
    public const string Claude = "Claude";
    public const string Fake = "Fake";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "openaicompatible" or "free" => OpenAICompatible,
        "claude" => Claude,
        "fake" => Fake,
        _ => throw new InvalidOperationException(
            $"Unknown Agent:Provider '{value}'. Allowed: {OpenAICompatible}, {Claude}, {Fake}."),
    };
}

/// <summary>
/// Any chat-completions API in the OpenAI format. The defaults point at the free tier of Google Gemini.
/// Other free options only need a different BaseUrl / Model / ApiKey (see README).
/// </summary>
public sealed class OpenAICompatibleOptions
{
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai/";

    /// <summary>
    /// Pick a model that has free quota in your account: https://aistudio.google.com/rate-limit
    /// Run the CLI with --list-models to see the exact ids.
    /// </summary>
    public string Model { get; set; } = "gemini-3.8-flash";

    /// <summary>
    /// Tried in order when the model is busy (503), out of free quota (429) or gone (404).
    /// Free quotas are per model, so a fallback usually still has quota left.
    /// The default list is in appsettings.json.
    /// </summary>
    public List<string> FallbackModels { get; set; } = [];

    /// <summary>
    /// An overloaded model may accept a request and answer very slowly. If there is a fallback left,
    /// switch to it after this many seconds. A normal roadmap takes 30–60 s.
    /// </summary>
    public int FallbackAfterSeconds { get; set; } = 90;

    /// <summary>If empty, the GEMINI_API_KEY environment variable is used. Local Ollama needs no key.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Output limit per request. Thinking models spend part of it on reasoning.</summary>
    public int MaxOutputTokens { get; set; } = 32000;

    /// <summary>Optional: minimal | low | medium | high. Empty — provider default. Not every provider supports it.</summary>
    public string? ReasoningEffort { get; set; }

    public int TimeoutSeconds { get; set; } = 300;
}

/// <summary>
/// Free web search for the research step (1,000 searches per month on the free plan).
/// Without a key the agent still works, just without fresh search results.
/// </summary>
public sealed class TavilyOptions
{
    /// <summary>If empty, the TAVILY_API_KEY environment variable is used.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Results per search query (each query costs 1 credit regardless of this number).</summary>
    public int MaxResultsPerQuery { get; set; } = 5;
}

public sealed class ClaudeOptions
{
    /// <summary>If empty, the SDK reads the ANTHROPIC_API_KEY environment variable.</summary>
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "claude-opus-5-5";

    /// <summary>low | medium | high | xhigh | max — how deeply the model thinks.</summary>
    public string Effort { get; set; } = "medium";

    /// <summary>Maximum web searches per research step (each ≈ $0.01).</summary>
    public int MaxSearches { get; set; } = 6;

    /// <summary>Retry on another model if the model refuses (refusal). Only where supported.</summary>
    public bool UseServerFallbacks { get; set; } = true;
}
