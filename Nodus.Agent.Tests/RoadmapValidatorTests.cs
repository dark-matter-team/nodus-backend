using Nodus.Agent.Models;
using Nodus.Agent.Pipeline;

namespace Nodus.Agent.Tests;

public class RoadmapValidatorTests
{
    private static RoadmapNode Node(string id, params string[] pre) => new()
    {
        Id = id,
        Title = id,
        Description = "d",
        Status = NodeStatus.Todo,
        Prerequisites = pre,
        EstimatedHours = 5,
        Steps = ["step"],
    };

    private static RoadmapDraft Draft(params RoadmapNode[] nodes) => new() { Summary = "s", Nodes = nodes };

    [Fact]
    public void Valid_graph_has_no_problems()
    {
        var draft = Draft(Node("a"), Node("b", "a"), Node("c", "a", "b"));
        Assert.Empty(RoadmapValidator.Validate(draft));
    }

    [Fact]
    public void Detects_cycle()
    {
        var draft = Draft(Node("a", "c"), Node("b", "a"), Node("c", "b"));
        Assert.Contains(RoadmapValidator.Validate(draft), p => p.Contains("cycle"));
    }

    [Fact]
    public void Detects_missing_prerequisite_and_self_reference()
    {
        var draft = Draft(Node("a", "a"), Node("b", "ghost"), Node("c"));
        var problems = RoadmapValidator.Validate(draft);
        Assert.Contains(problems, p => p.Contains("depends on itself"));
        Assert.Contains(problems, p => p.Contains("ghost"));
    }

    [Fact]
    public void Detects_duplicate_and_bad_ids()
    {
        var draft = Draft(Node("a"), Node("a"), Node("Bad Id"));
        var problems = RoadmapValidator.Validate(draft);
        Assert.Contains(problems, p => p.Contains("duplicated"));
        Assert.Contains(problems, p => p.Contains("kebab-case"));
    }

    [Fact]
    public void Detects_missing_steps_and_too_long_texts()
    {
        var noSteps = Node("a") with { Steps = [] };
        var longTitle = Node("b") with { Title = new string('x', RoadmapValidator.MaxTextLength + 1) };
        var longStep = Node("c") with { Steps = [new string('x', RoadmapValidator.MaxTextLength + 1)] };

        var problems = RoadmapValidator.Validate(Draft(noSteps, longTitle, longStep));

        Assert.Contains(problems, p => p.Contains("\"a\" has 0 steps"));
        Assert.Contains(problems, p => p.Contains("\"b\" has a title longer"));
        Assert.Contains(problems, p => p.Contains("\"c\" has an empty step or a step longer"));
    }

    [Fact]
    public void Detects_bad_resource_url()
    {
        var node = Node("a") with
        {
            Resources = [new LearningResource { Title = "x", Url = "javascript:alert(1)", Type = "docs" }],
        };
        Assert.Contains(RoadmapValidator.Validate(Draft(node, Node("b"), Node("c"))), p => p.Contains("invalid link"));
    }
}
