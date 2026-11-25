using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nodus.Agent.Claude;
using Nodus.Agent.Pipeline;

namespace Nodus.Agent.OpenAICompatible;

public sealed record ChatResult(string Content, string? FinishReason, long PromptTokens, long CompletionTokens, string Model);

/// <summary>
/// A minimal client for the OpenAI chat-completions format (POST {BaseUrl}chat/completions).
/// Works with the free tiers of Gemini, Groq and OpenRouter, and with a local Ollama.
///
/// Free models are often busy or out of quota, so the client:
/// - retries 429 and 5xx responses with a pause;
/// - switches to the next model in FallbackModels when a model stays unavailable or is too slow,
///   and keeps using the one that worked for a while before trying the main model again;
/// - degrades structured outputs step by step and remembers what worked:
///   json_schema → json_object → plain text with the schema in the prompt.
/// </summary>
public sealed class OpenAICompatibleChatClient(
    IHttpClientFactory httpFactory,
    IOptions<AgentOptions> options,
    TimeProvider time,
    ILogger<OpenAICompatibleChatClient> logger)
{
    public const string HttpClientName = "openai-compatible";

    /// <summary>Retries per model when it is the last one to try.</summary>
    private const int MaxRetries = 3;

    /// <summary>With another model to switch to, waiting long for a busy one is not worth it.</summary>
    private const int RetriesBeforeFallback = 1;

    /// <summary>How long to stay on a fallback model before trying the main model again.</summary>
    private static readonly TimeSpan StickToFallback = TimeSpan.FromMinutes(10);

    private enum JsonMode { Schema, Object, PromptOnly }

    private int _jsonMode = (int)JsonMode.Schema;
    private Preference? _preferred;

    private sealed record Preference(string Model, DateTimeOffset Until);

    public async Task<ChatResult> CompleteJsonAsync(string system, string user, string schemaJson, CancellationToken ct)
    {
        EnsureApiKey();
        var models = ModelsInOrder();
        for (var i = 0; ; i++)
        {
            var model = models[i];
            var hasNext = i + 1 < models.Count;
            try
            {
                var result = await CompleteWithModelAsync(model, system, user, schemaJson, hasNext, ct);
                Remember(model);
                return result;
            }
            catch (ModelUnavailableException e) when (hasNext)
            {
                logger.LogWarning("Model {Model} is unavailable ({Reason}); switching to {Next}",
                    model, e.Message, models[i + 1]);
            }
        }
    }

    /// <summary>Model ids available for the key (GET {BaseUrl}models).</summary>
    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
    {
        EnsureApiKey();
        var http = httpFactory.CreateClient(HttpClientName);
        using var response = await http.GetAsync("models", ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw ToException(response.StatusCode, text, options.Value.OpenAICompatible.Model, hadResponseFormat: false);
        }

        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.GetProperty("data").EnumerateArray()
            .Select(m => m.GetProperty("id").GetString() ?? "")
            .Where(id => id.Length > 0)
            .Order()
            .ToList();
    }

    /// <summary>The main model and the fallbacks; a fallback that worked recently goes first.</summary>
    private List<string> ModelsInOrder()
    {
        var o = options.Value.OpenAICompatible;
        var models = new[] { o.Model }.Concat(o.FallbackModels)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var preferred = Volatile.Read(ref _preferred);
        if (preferred is not null && preferred.Until > time.GetUtcNow() && models.Remove(preferred.Model))
        {
            models.Insert(0, preferred.Model);
        }

        return models;
    }

    private void Remember(string model)
    {
        var isMain = string.Equals(model, options.Value.OpenAICompatible.Model, StringComparison.OrdinalIgnoreCase);
        Volatile.Write(ref _preferred, isMain ? null : new Preference(model, time.GetUtcNow() + StickToFallback));
    }

    private async Task<ChatResult> CompleteWithModelAsync(
        string model, string system, string user, string schemaJson, bool hasNext, CancellationToken ct)
    {
        while (true)
        {
            var mode = (JsonMode)Volatile.Read(ref _jsonMode);
            try
            {
                return await SendAsync(BuildBody(model, system, user, schemaJson, mode), model, hasNext, ct);
            }
            catch (ResponseFormatRejectedException e) when (mode != JsonMode.PromptOnly)
            {
                logger.LogWarning("Model {Model} rejected response_format in mode {Mode} ({Error}); falling back",
                    model, mode, e.Message);
                Interlocked.CompareExchange(ref _jsonMode, (int)mode + 1, (int)mode);
            }
        }
    }

    /// <summary>A cloud provider without a key would just answer 401 — say what to do instead. Local Ollama needs no key.</summary>
    private void EnsureApiKey()
    {
        var o = options.Value.OpenAICompatible;
        var isLocal = Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var uri) && uri.IsLoopback;
        if (string.IsNullOrWhiteSpace(o.ApiKey) && !isLocal)
        {
            throw new RoadmapGenerationException(
                "The free model has no API key yet. Set Agent:OpenAICompatible:ApiKey (README → \"Free setup\").");
        }
    }

    private JsonObject BuildBody(string model, string system, string user, string schemaJson, JsonMode mode)
    {
        var o = options.Value.OpenAICompatible;
        var systemText = mode == JsonMode.Schema ? system : system + "\n\n" + Prompts.JsonOnly(schemaJson);

        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = o.MaxOutputTokens,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = systemText },
                new JsonObject { ["role"] = "user", ["content"] = user }),
        };

        if (!string.IsNullOrWhiteSpace(o.ReasoningEffort))
        {
            body["reasoning_effort"] = o.ReasoningEffort;
        }

        switch (mode)
        {
            case JsonMode.Schema:
                body["response_format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JsonObject
                    {
                        ["name"] = "roadmap",
                        ["strict"] = true,
                        ["schema"] = JsonNode.Parse(schemaJson),
                    },
                };
                break;
            case JsonMode.Object:
                body["response_format"] = new JsonObject { ["type"] = "json_object" };
                break;
        }

        return body;
    }

    private async Task<ChatResult> SendAsync(JsonObject body, string model, bool hasNext, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(HttpClientName);
        var hadResponseFormat = body.ContainsKey("response_format");
        var maxRetries = hasNext ? RetriesBeforeFallback : MaxRetries;

        for (var attempt = 1; ; attempt++)
        {
            // With a fallback left, a model that is too slow counts as unavailable.
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (hasNext)
            {
                attemptCts.CancelAfter(TimeSpan.FromSeconds(options.Value.OpenAICompatible.FallbackAfterSeconds));
            }

            HttpResponseMessage response;
            string text;
            try
            {
                response = await http.PostAsync("chat/completions", JsonContent.Create(body), attemptCts.Token);
                text = await response.Content.ReadAsStringAsync(attemptCts.Token);
            }
            catch (OperationCanceledException) when (hasNext && !ct.IsCancellationRequested)
            {
                throw new ModelUnavailableException(
                    $"no answer in {options.Value.OpenAICompatible.FallbackAfterSeconds} s");
            }

            using var _ = response;
            if (response.IsSuccessStatusCode)
            {
                return Parse(text, model);
            }

            if (IsRetryable(response.StatusCode) && attempt <= maxRetries)
            {
                var delay = RetryDelay(response, attempt);
                logger.LogWarning("Model {Model} returned {Status}, retry #{Attempt} in {Delay:0} s",
                    model, (int)response.StatusCode, attempt, delay.TotalSeconds);
                await Task.Delay(delay, time, ct);
                continue;
            }

            if (hasNext && (IsRetryable(response.StatusCode) || response.StatusCode == HttpStatusCode.NotFound))
            {
                logger.LogWarning("Model {Model} error {Status}: {Body}", model, (int)response.StatusCode, Trim(text, 300));
                throw new ModelUnavailableException($"HTTP {(int)response.StatusCode}");
            }

            throw ToException(response.StatusCode, text, model, hadResponseFormat);
        }
    }

    private static ChatResult Parse(string text, string model)
    {
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var choice = root.GetProperty("choices")[0];
        var message = choice.GetProperty("message");

        var content = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString()!
            : "";
        var finish = choice.TryGetProperty("finish_reason", out var f) && f.ValueKind == JsonValueKind.String
            ? f.GetString()
            : null;

        long prompt = 0, completion = 0;
        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            prompt = usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt64(out var pv) ? pv : 0;
            completion = usage.TryGetProperty("completion_tokens", out var cpl) && cpl.TryGetInt64(out var cv) ? cv : 0;
        }

        return new ChatResult(content, finish, prompt, completion, model);
    }

    private static bool IsRetryable(HttpStatusCode status) =>
        status is HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    private static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return delta <= TimeSpan.FromSeconds(60) ? delta : TimeSpan.FromSeconds(60);
        }

        return TimeSpan.FromSeconds(Math.Min(30, 5 * Math.Pow(2, attempt - 1)));
    }

    private Exception ToException(HttpStatusCode status, string body, string model, bool hadResponseFormat)
    {
        logger.LogWarning("Model {Model} error {Status}: {Body}", model, (int)status, Trim(body, 1000));

        if (status == HttpStatusCode.BadRequest && hadResponseFormat &&
            (body.Contains("response_format", StringComparison.OrdinalIgnoreCase) ||
             body.Contains("json_schema", StringComparison.OrdinalIgnoreCase) ||
             body.Contains("schema", StringComparison.OrdinalIgnoreCase)))
        {
            return new ResponseFormatRejectedException(Trim(body, 300));
        }

        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new RoadmapGenerationException(
                "The model provider rejected the API key. Check Agent:OpenAICompatible:ApiKey (or GEMINI_API_KEY)."),
            HttpStatusCode.NotFound => new RoadmapGenerationException(
                $"Model '{model}' was not found at the provider. Check Agent:OpenAICompatible:Model (the CLI lists models with --list-models)."),
            HttpStatusCode.TooManyRequests => new RoadmapGenerationException(
                "The free model's rate limit or daily quota is used up. Try again later or switch to another free model."),
            HttpStatusCode.ServiceUnavailable => new RoadmapGenerationException(
                "The free model is overloaded right now. Try again in a few minutes."),
            _ => new RoadmapGenerationException($"The model provider returned an error ({(int)status}). Please try again later."),
        };
    }

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    private sealed class ResponseFormatRejectedException(string message) : Exception(message);

    private sealed class ModelUnavailableException(string reason) : Exception(reason);
}
