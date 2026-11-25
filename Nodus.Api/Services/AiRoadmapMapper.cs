using System.Text.RegularExpressions;
using Nodus.Api.Data;
using Nodus.Api.DTOs;
using Nodus.Api.Entities;
using Nodus.Api.Enums;
using AgentModels = Nodus.Agent.Models;

namespace Nodus.Api.Services;

/// <summary>
/// Converts the request into the agent's profile and the agent's roadmap into database entities:
/// string ids -> Guids, prerequisites -> a single parent (tree), statuses, and a starting X/Y layout.
/// </summary>
public static partial class AiRoadmapMapper
{
    public const double LevelSpacingX = 280;
    public const double SiblingSpacingY = 160;

    private const int DefaultHoursPerWeek = 5;
    private const int MaxTags = 5;
    private const int MaxTagLength = 30;

    [GeneratedRegex(@"\d+")]
    private static partial Regex FirstNumber();

    [GeneratedRegex(@"\p{IsCyrillic}")]
    private static partial Regex Cyrillic();

    public static AgentModels.UserProfile ToProfile(GenerateRoadmapRequest request, string goal) => new()
    {
        Goal = goal,
        Background = $"Self-assessed overall level for this goal: {request.KnowledgeLevel}.",
        HoursPerWeek = ParseHoursPerWeek(request.TimeCommitment),
        // The roadmap is written in the language of the goal itself.
        Language = Cyrillic().IsMatch(goal) ? "uk" : "en",
        FreeResourcesOnly = true,
    };

    /// <summary>"5 hrs/week" -> 5. Anything without a number falls back to a sensible default.</summary>
    public static int ParseHoursPerWeek(string? timeCommitment)
    {
        var match = FirstNumber().Match(timeCommitment ?? string.Empty);
        return match.Success && int.TryParse(match.Value, out var hours) && hours > 0
            ? Math.Min(hours, 80)
            : DefaultHoursPerWeek;
    }

    public static Roadmap ToEntity(
        AgentModels.LearningRoadmap source, Guid userId, GenerateRoadmapRequest request, string goal, string timeCommitment)
    {
        var parentOf = CollapseToTree(source.Nodes);
        var ids = source.Nodes.ToDictionary(n => n.Id, _ => Guid.NewGuid());
        var nodes = new Dictionary<string, RoadmapNode>();

        // Parents are always created before their children, so statuses can follow the parent.
        foreach (var agentNode in TopologicalOrder(source.Nodes, parentOf))
        {
            var parentId = parentOf[agentNode.Id];
            var parent = parentId is null ? null : nodes[parentId];
            var parentDone = parent is null || parent.Status == NodeStatus.Completed;

            // "known" topics count as done only if the path to them is done too; otherwise the tree
            // would contain completed nodes behind locked ones.
            var completed = agentNode.Status == AgentModels.NodeStatus.Known && parentDone;
            var status = completed ? NodeStatus.Completed : parentDone ? NodeStatus.Available : NodeStatus.Locked;

            nodes[agentNode.Id] = new RoadmapNode
            {
                Id = ids[agentNode.Id],
                ParentNodeId = parent?.Id,
                Title = Truncate(agentNode.Title.Trim(), FieldLimits.Title),
                Description = string.Join(" ", new[] { agentNode.Description, agentNode.Why }
                    .Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim())),
                Category = agentNode.Category switch
                {
                    AgentModels.NodeCategory.Practice => NodeCategory.Practice,
                    AgentModels.NodeCategory.Project => NodeCategory.Project,
                    _ => NodeCategory.Theory,
                },
                EstimatedMinutes = agentNode.EstimatedHours > 0 ? agentNode.EstimatedHours * 60 : 30,
                Status = status,
                CompletedAt = completed ? DateTime.UtcNow : null,
                ChecklistItems = agentNode.Steps
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select((s, i) => new NodeChecklistItem
                    {
                        Title = Truncate(s.Trim(), FieldLimits.Title),
                        IsCompleted = completed,
                        SortOrder = i + 1,
                    })
                    .ToList(),
                Resources = agentNode.Resources
                    .Select(r => new NodeResource { Title = r.Title, Url = r.Url, Type = r.Type, IsFree = r.Free })
                    .ToList(),
            };
        }

        var orderedNodes = source.Nodes.Select(n => nodes[n.Id]).ToList();
        ApplyTreeLayout(orderedNodes);

        return new Roadmap
        {
            OwnerId = userId,
            Title = Truncate(goal, FieldLimits.Title),
            GoalPrompt = goal,
            Description = string.IsNullOrWhiteSpace(source.Summary) ? null : source.Summary.Trim(),
            KnowledgeLevel = request.KnowledgeLevel,
            TimeCommitment = timeCommitment,
            Status = orderedNodes.All(n => n.Status == NodeStatus.Completed) ? RoadmapStatus.Completed : RoadmapStatus.Active,
            Tags = NormalizeTags(source.Tags),
            Nodes = orderedNodes,
        };
    }

    /// <summary>
    /// The app stores a tree (one parent per node). If the model still returns several prerequisites,
    /// the deepest one is kept as the parent: it is the last thing to learn before this node.
    /// </summary>
    public static Dictionary<string, string?> CollapseToTree(IReadOnlyList<AgentModels.RoadmapNode> nodes)
    {
        var byId = nodes.ToDictionary(n => n.Id);
        var depth = new Dictionary<string, int>();

        int DepthOf(string id, HashSet<string> visiting)
        {
            if (depth.TryGetValue(id, out var known)) return known;
            if (!visiting.Add(id)) return 0; // defensive: the validator already rejects cycles

            var prerequisites = byId[id].Prerequisites.Where(p => p != id && byId.ContainsKey(p)).ToList();
            var result = prerequisites.Count == 0 ? 0 : 1 + prerequisites.Max(p => DepthOf(p, visiting));

            visiting.Remove(id);
            depth[id] = result;
            return result;
        }

        return nodes.ToDictionary(
            n => n.Id,
            n => n.Prerequisites
                .Where(p => p != n.Id && byId.ContainsKey(p))
                .OrderByDescending(p => DepthOf(p, []))
                .FirstOrDefault());
    }

    /// <summary>
    /// Left-to-right tidy tree: X by depth, leaves get consecutive rows, a parent sits in the middle of its children.
    /// </summary>
    public static void ApplyTreeLayout(IReadOnlyList<RoadmapNode> nodes)
    {
        var children = nodes
            .Where(n => n.ParentNodeId.HasValue)
            .GroupBy(n => n.ParentNodeId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var nextRow = 0;

        double Place(RoadmapNode node, int depth)
        {
            node.PositionX = depth * LevelSpacingX;

            if (!children.TryGetValue(node.Id, out var kids))
            {
                node.PositionY = nextRow++ * SiblingSpacingY;
                return node.PositionY;
            }

            var ys = kids.Select(k => Place(k, depth + 1)).ToList();
            node.PositionY = (ys.First() + ys.Last()) / 2;
            return node.PositionY;
        }

        foreach (var root in nodes.Where(n => n.ParentNodeId is null))
            Place(root, 0);
    }

    private static IEnumerable<AgentModels.RoadmapNode> TopologicalOrder(
        IReadOnlyList<AgentModels.RoadmapNode> nodes, Dictionary<string, string?> parentOf)
    {
        var emitted = new HashSet<string>();
        var byId = nodes.ToDictionary(n => n.Id);

        IEnumerable<AgentModels.RoadmapNode> Visit(AgentModels.RoadmapNode node)
        {
            if (emitted.Contains(node.Id)) yield break;

            if (parentOf[node.Id] is { } parentId && !emitted.Contains(parentId))
                foreach (var ancestor in Visit(byId[parentId]))
                    yield return ancestor;

            if (emitted.Add(node.Id))
                yield return node;
        }

        return nodes.SelectMany(Visit).ToList();
    }

    private static List<string> NormalizeTags(IEnumerable<string> tags) => tags
        .Select(t => t.Trim().ToLowerInvariant())
        .Where(t => t.Length is > 0 and <= MaxTagLength)
        .Distinct()
        .Take(MaxTags)
        .ToList();

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
