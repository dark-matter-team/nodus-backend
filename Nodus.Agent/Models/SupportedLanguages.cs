namespace Nodus.Agent.Models;

/// <summary>
/// Languages a roadmap can be generated in. The API accepts the code, the model gets the name:
/// "uk" alone is ambiguous for a model (Ukrainian vs. the United Kingdom).
/// </summary>
public static class SupportedLanguages
{
    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        ["en"] = "English",
        ["uk"] = "Ukrainian",
    };

    public static bool IsSupported(string code) => Names.ContainsKey(code);

    public static string NameOf(string code) =>
        Names.TryGetValue(code, out var name) ? name : throw new ArgumentException($"Unsupported language '{code}'.");
}
