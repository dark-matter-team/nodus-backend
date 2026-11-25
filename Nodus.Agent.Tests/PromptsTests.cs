using Nodus.Agent.Claude;
using Nodus.Agent.Models;
using Nodus.Agent.Pipeline;

namespace Nodus.Agent.Tests;

public class PromptsTests
{
    private static readonly ResearchResult Research = new(
        "notes", [new SourceLink("Docs", "https://example.com")], UsageReport.Empty, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Prompts_name_the_language_instead_of_the_ambiguous_code()
    {
        var profile = new UserProfile { Goal = "Data Engineer", Language = "uk" };

        var research = Prompts.ResearchUser(new ResearchRequest(profile.Goal, profile.Language, false), new DateOnly(2026, 11, 10));
        var build = Prompts.BuildUser(new BuildRequest(profile, Research));

        Assert.Contains("Preferred materials language: Ukrainian", research);
        Assert.Contains("## Output language\nUkrainian", build.ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("uk", true)]
    [InlineData("ru", false)]
    public void Only_english_and_ukrainian_are_supported(string code, bool supported) =>
        Assert.Equal(supported, SupportedLanguages.IsSupported(code));
}
