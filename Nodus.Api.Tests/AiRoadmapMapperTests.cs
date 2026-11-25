using Nodus.Api.DTOs;
using Nodus.Domain.Enums;
using Nodus.Api.Services;
using AgentModels = Nodus.Agent.Models;

namespace Nodus.Api.Tests;

public class AiRoadmapMapperTests
{
    private static readonly GenerateRoadmapRequest Request = new("Learn Python", KnowledgeLevel.Beginner, "5 hrs/week");

    private static AgentModels.RoadmapNode Node(
        string id, string[]? pre = null, AgentModels.NodeStatus status = AgentModels.NodeStatus.Todo) => new()
    {
        Id = id,
        Title = id,
        Description = "d",
        Why = "w",
        Status = status,
        Prerequisites = pre ?? [],
        EstimatedHours = 2,
        Steps = ["step 1", "step 2"],
    };

    private static AgentModels.LearningRoadmap Roadmap(params AgentModels.RoadmapNode[] nodes) => new()
    {
        Goal = "Learn Python",
        Summary = "Summary",
        Tags = ["Python", " python ", "Data"],
        Nodes = nodes,
    };

    [Theory]
    [InlineData("5 hrs/week", 5)]
    [InlineData("10", 10)]
    [InlineData("", 5)]
    [InlineData("a lot", 5)]
    [InlineData("200 hrs/week", 80)]
    public void Parses_hours_per_week(string input, int expected) =>
        Assert.Equal(expected, AiRoadmapMapper.ParseHoursPerWeek(input));

    [Theory]
    [InlineData("Learn Python", "en")]
    [InlineData("Вивчити Python", "uk")]
    public void Picks_language_of_the_goal(string goal, string expected) =>
        Assert.Equal(expected, AiRoadmapMapper.ToProfile(Request, goal).Language);

    [Fact]
    public void Several_prerequisites_collapse_to_the_deepest_one()
    {
        // a -> b -> c, and d requires both a and c: c is deeper, so it becomes the parent.
        var parents = AiRoadmapMapper.CollapseToTree(
            [Node("a"), Node("b", ["a"]), Node("c", ["b"]), Node("d", ["a", "c"])]);

        Assert.Null(parents["a"]);
        Assert.Equal("c", parents["d"]);
    }

    [Fact]
    public void Maps_ids_statuses_checklist_and_tags()
    {
        var roadmap = AiRoadmapMapper.ToEntity(
            Roadmap(Node("a"), Node("b", ["a"])), Guid.NewGuid(), Request, "Learn Python", "5 hrs/week");

        var root = roadmap.Nodes[0];
        var child = roadmap.Nodes[1];

        Assert.Equal(root.Id, child.ParentNodeId);
        Assert.NotEqual(Guid.Empty, root.Id);
        Assert.Equal(NodeStatus.Available, root.Status);
        Assert.Equal(NodeStatus.Locked, child.Status);
        Assert.Equal(["step 1", "step 2"], root.ChecklistItems.Select(c => c.Title));
        Assert.Equal(120, root.EstimatedMinutes);
        Assert.Equal("d w", root.Description);
        Assert.Equal(["python", "data"], roadmap.Tags);
        Assert.Equal(RoadmapStatus.Active, roadmap.Status);
    }

    [Fact]
    public void Known_topics_are_completed_only_when_their_parent_is_done()
    {
        var roadmap = AiRoadmapMapper.ToEntity(
            Roadmap(
                Node("a", status: AgentModels.NodeStatus.Known),
                Node("b", ["a"]),
                Node("c", ["b"], AgentModels.NodeStatus.Known)),
            Guid.NewGuid(), Request, "Learn Python", "5");

        Assert.Equal(NodeStatus.Completed, roadmap.Nodes[0].Status);
        Assert.All(roadmap.Nodes[0].ChecklistItems, i => Assert.True(i.IsCompleted));
        Assert.Equal(NodeStatus.Available, roadmap.Nodes[1].Status); // unlocked by the known parent
        Assert.Equal(NodeStatus.Locked, roadmap.Nodes[2].Status);    // known, but behind an unfinished node
    }

    [Fact]
    public void Child_listed_before_parent_still_gets_the_parent_status()
    {
        var roadmap = AiRoadmapMapper.ToEntity(
            Roadmap(Node("b", ["a"]), Node("a", status: AgentModels.NodeStatus.Known)),
            Guid.NewGuid(), Request, "Learn Python", "5");

        Assert.Equal(NodeStatus.Available, roadmap.Nodes[0].Status);
    }

    [Fact]
    public void Layout_places_levels_left_to_right_and_parent_between_children()
    {
        var roadmap = AiRoadmapMapper.ToEntity(
            Roadmap(Node("root"), Node("left", ["root"]), Node("right", ["root"]), Node("leaf", ["left"])),
            Guid.NewGuid(), Request, "Learn Python", "5");

        var byTitle = roadmap.Nodes.ToDictionary(n => n.Title);

        Assert.Equal(0, byTitle["root"].PositionX);
        Assert.Equal(AiRoadmapMapper.LevelSpacingX, byTitle["left"].PositionX);
        Assert.Equal(2 * AiRoadmapMapper.LevelSpacingX, byTitle["leaf"].PositionX);
        Assert.Equal(
            (byTitle["left"].PositionY + byTitle["right"].PositionY) / 2,
            byTitle["root"].PositionY);
        Assert.NotEqual(byTitle["left"].PositionY, byTitle["right"].PositionY);
    }
}
