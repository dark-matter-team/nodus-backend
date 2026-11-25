using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nodus.Agent;

/// <summary>
/// Shared JSON settings: snake_case and enums as strings ("todo", "known").
/// Used both by the API and when parsing the model's response.
/// </summary>
public static class RoadmapJson
{
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions
    {
        WriteIndented = true,
    });

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        // Non-ASCII text (e.g. Ukrainian) as-is instead of \uXXXX escapes: readable JSON and no wasted tokens in prompts.
        options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.PropertyNameCaseInsensitive = true;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}
