using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nodus.Agent.Models;
using Nodus.Agent.Pipeline;

namespace Nodus.Agent.Tests;

public class PipelineTests
{
    private static RoadmapGenerator CreateFakeGenerator()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:Provider"] = "Fake" })
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddRoadmapAgent(config)
            .BuildServiceProvider()
            .GetRequiredService<RoadmapGenerator>();
    }

    [Fact]
    public async Task Fake_pipeline_builds_valid_personalized_roadmap()
    {
        var generator = CreateFakeGenerator();
        var profile = new UserProfile
        {
            Goal = "Data Engineer",
            Skills = [new SkillLevel("Git", 3), new SkillLevel("Python", 1)],
        };

        var messages = new List<string>();
        var result = await generator.GenerateAsync(profile, new SyncProgress(messages.Add));

        var roadmap = result.Roadmap;
        Assert.Equal("Data Engineer", roadmap.Goal);
        Assert.Empty(RoadmapValidator.Validate(new RoadmapDraft { Summary = roadmap.Summary, Nodes = roadmap.Nodes }));
        Assert.Equal(NodeStatus.Known, roadmap.Nodes.Single(n => n.Id == "git").Status);
        Assert.Equal(NodeStatus.Refresh, roadmap.Nodes.Single(n => n.Id == "python-basics").Status);
        Assert.Equal(0, roadmap.Nodes.Single(n => n.Id == "git").EstimatedHours);
        Assert.NotEmpty(messages);
    }

    [Fact]
    public async Task Missed_deadline_is_reported_by_code_not_by_the_model()
    {
        var generator = CreateFakeGenerator();

        var tight = await generator.GenerateAsync(new UserProfile { Goal = "Data Engineer", HoursPerWeek = 1, DeadlineWeeks = 1 });
        var relaxed = await generator.GenerateAsync(new UserProfile { Goal = "Data Engineer", HoursPerWeek = 40, DeadlineWeeks = 100 });

        Assert.Contains(tight.Warnings, w => w.Contains("the deadline allows 1 h"));
        Assert.DoesNotContain(relaxed.Warnings, w => w.Contains("deadline"));
    }

    [Fact]
    public async Task Research_is_cached_by_goal()
    {
        var generator = CreateFakeGenerator();
        var first = await generator.GenerateAsync(new UserProfile { Goal = "Data Engineer" });
        var second = await generator.GenerateAsync(new UserProfile { Goal = "  data engineer " });

        // The second time there was no research — it came from the cache.
        Assert.Equal(first.Roadmap.Sources, second.Roadmap.Sources);
    }

    [Fact]
    public void Roadmap_serializes_in_snake_case()
    {
        var node = new RoadmapNode
        {
            Id = "a", Title = "A", Description = "d", Status = NodeStatus.Refresh, EstimatedHours = 3,
        };
        var json = System.Text.Json.JsonSerializer.Serialize(node, RoadmapJson.Options);
        Assert.Contains("\"estimated_hours\": 3", json);
        Assert.Contains("\"status\": \"refresh\"", json);
    }

    private sealed class SyncProgress(Action<string> onReport) : IProgress<string>
    {
        public void Report(string value) => onReport(value);
    }
}
