using System.Text.Json;
using Anthropic.Models.Beta.Messages;
using Nodus.Agent.Claude;
using Nodus.Agent.Pipeline;

namespace Nodus.Agent.Tests;

/// <summary>
/// Tests handling of a Claude response without network access, using a sample web search response.
/// </summary>
public class ClaudeSupportTests
{
    private const string SampleResponse = """
        {
          "id": "msg_test",
          "type": "message",
          "role": "assistant",
          "model": "claude-opus-5-5",
          "content": [
            { "type": "text", "text": "Looking up requirements. " },
            {
              "type": "server_tool_use",
              "id": "srvtoolu_1",
              "name": "web_search",
              "input": { "query": "data engineer skills 2026" }
            },
            {
              "type": "web_search_tool_result",
              "tool_use_id": "srvtoolu_1",
              "content": [
                { "type": "web_search_result", "url": "https://example.com/de", "title": "DE skills", "encrypted_content": "ENC123", "page_age": "October 1, 2026" },
                { "type": "web_search_result", "url": "https://example.org/spark", "title": "Spark", "encrypted_content": "ENC456", "page_age": null }
              ]
            },
            {
              "type": "text",
              "text": "Spark is required almost everywhere.",
              "citations": [
                { "type": "web_search_result_location", "url": "https://example.org/spark", "title": "Spark", "encrypted_index": "IDX1", "cited_text": "Spark is used" }
              ]
            }
          ],
          "stop_reason": "pause_turn",
          "stop_sequence": null,
          "usage": { "input_tokens": 1000, "output_tokens": 200, "server_tool_use": { "web_search_requests": 2 } }
        }
        """;

    private static BetaMessage Sample() => JsonSerializer.Deserialize<BetaMessage>(SampleResponse)!;

    [Fact]
    public void Extracts_text_sources_and_usage()
    {
        var message = Sample();

        Assert.Equal("Looking up requirements. Spark is required almost everywhere.", ClaudeSupport.CollectText(message.Content));
        Assert.True(message.StopReason == "pause_turn");

        var sources = ClaudeSupport.ExtractSources(message.Content);
        // The cited source comes first, duplicates are removed.
        Assert.Equal(["https://example.org/spark", "https://example.com/de"], sources.Select(s => s.Url));

        var usage = ClaudeSupport.ToUsage(message.Usage);
        Assert.Equal(new UsageReport(1000, 200, 0, 2), usage);
    }

    [Fact]
    public void Round_trips_blocks_for_pause_turn_without_losing_encrypted_fields()
    {
        var parameters = ClaudeSupport.ToParams(Sample().Content);
        Assert.Equal(4, parameters.Count);

        var json = JsonSerializer.Serialize(parameters);
        Assert.Contains("\"server_tool_use\"", json);
        Assert.Contains("\"ENC123\"", json);
        Assert.Contains("\"IDX1\"", json);
        Assert.Contains("\"srvtoolu_1\"", json);
    }

    [Fact]
    public void Effort_parsing()
    {
        Assert.Equal(Effort.Medium, ClaudeSupport.ParseEffort("Medium"));
        Assert.Throws<ArgumentException>(() => ClaudeSupport.ParseEffort("super"));
    }

    [Theory]
    [InlineData("https://docs.python.org/3/", true)]
    [InlineData("http://localhost:8080/admin", false)]
    [InlineData("http://169.254.169.254/metadata", false)]
    [InlineData("http://intranet/", false)]
    [InlineData("ftp://example.com/file", false)]
    public void Link_checker_refuses_internal_addresses(string url, bool safe) =>
        Assert.Equal(safe, HttpLinkChecker.IsSafeToFetch(url));
}
