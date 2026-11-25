using System.Text.Json;

namespace Nodus.Agent.Claude;

/// <summary>
/// JSON Schema of the build step response. Structured outputs guarantee
/// that the model returns JSON of exactly this shape.
/// Limitations: every object needs additionalProperties = false,
/// minimum/maxLength etc. are not supported (RoadmapValidator checks those).
/// </summary>
public static class RoadmapSchema
{
    public const string Json = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["summary", "tags", "nodes"],
          "properties": {
            "summary": { "type": "string" },
            "tags": { "type": "array", "items": { "type": "string" } },
            "nodes": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["id", "title", "description", "status", "category", "prerequisites", "estimated_hours", "why", "steps", "resources"],
                "properties": {
                  "id": { "type": "string" },
                  "title": { "type": "string" },
                  "description": { "type": "string" },
                  "status": { "type": "string", "enum": ["known", "refresh", "todo"] },
                  "category": { "type": "string", "enum": ["theory", "practice", "project"] },
                  "steps": { "type": "array", "items": { "type": "string" } },
                  "prerequisites": { "type": "array", "items": { "type": "string" } },
                  "estimated_hours": { "type": "integer" },
                  "why": { "type": "string" },
                  "resources": {
                    "type": "array",
                    "items": {
                      "type": "object",
                      "additionalProperties": false,
                      "required": ["title", "url", "type", "free"],
                      "properties": {
                        "title": { "type": "string" },
                        "url": { "type": "string", "format": "uri" },
                        "type": { "type": "string", "enum": ["docs", "course", "book", "video", "article", "practice"] },
                        "free": { "type": "boolean" }
                      }
                    }
                  }
                }
              }
            }
          }
        }
        """;

    /// <summary>
    /// The same schema without "format" keywords: some OpenAI-compatible providers reject them.
    /// URLs are checked by RoadmapValidator anyway.
    /// </summary>
    public static readonly string PortableJson = Json.Replace(", \"format\": \"uri\"", "");

    public static Dictionary<string, JsonElement> AsDictionary() =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Json)!;
}
