using System.Text.RegularExpressions;
using Nodus.Agent.Models;

namespace Nodus.Agent.Pipeline;

/// <summary>
/// Checks the graph in code. The model can make mistakes — this is where we catch them.
/// Messages are written so they can be sent back to the model to fix.
/// </summary>
public static partial class RoadmapValidator
{
    public const int MinNodes = 3;
    public const int MaxNodes = 50;

    /// <summary>Matches the backend column limit for titles and checklist items.</summary>
    public const int MaxTextLength = 200;
    public const int MaxSteps = 8;

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex IdPattern();

    public static IReadOnlyList<string> Validate(RoadmapDraft draft)
    {
        var problems = new List<string>();
        var nodes = draft.Nodes;

        if (string.IsNullOrWhiteSpace(draft.Summary))
        {
            problems.Add("The summary field is empty.");
        }

        if (nodes.Count < MinNodes || nodes.Count > MaxNodes)
        {
            problems.Add($"There are {nodes.Count} nodes, expected {MinNodes} to {MaxNodes}.");
        }

        var ids = new HashSet<string>();
        foreach (var node in nodes)
        {
            if (!IdPattern().IsMatch(node.Id))
            {
                problems.Add($"id \"{node.Id}\" must be lowercase kebab-case.");
            }

            if (!ids.Add(node.Id))
            {
                problems.Add($"id \"{node.Id}\" is duplicated.");
            }

            if (string.IsNullOrWhiteSpace(node.Title))
            {
                problems.Add($"Node \"{node.Id}\" has an empty title.");
            }
            else if (node.Title.Length > MaxTextLength)
            {
                problems.Add($"Node \"{node.Id}\" has a title longer than {MaxTextLength} characters.");
            }

            if (node.Steps.Count is < 1 or > MaxSteps)
            {
                problems.Add($"Node \"{node.Id}\" has {node.Steps.Count} steps, expected 1 to {MaxSteps}.");
            }
            else if (node.Steps.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > MaxTextLength))
            {
                problems.Add($"Node \"{node.Id}\" has an empty step or a step longer than {MaxTextLength} characters.");
            }

            if (node.EstimatedHours is < 0 or > 500)
            {
                problems.Add($"Node \"{node.Id}\" has an unrealistic estimated_hours = {node.EstimatedHours}.");
            }

            foreach (var r in node.Resources)
            {
                if (!Uri.TryCreate(r.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                {
                    problems.Add($"Node \"{node.Id}\" has an invalid link \"{r.Url}\".");
                }
            }
        }

        foreach (var node in nodes)
        {
            foreach (var pre in node.Prerequisites)
            {
                if (pre == node.Id)
                {
                    problems.Add($"Node \"{node.Id}\" depends on itself.");
                }
                else if (!ids.Contains(pre))
                {
                    problems.Add($"Node \"{node.Id}\" references a non-existent node \"{pre}\".");
                }
            }
        }

        var cycle = FindCycle(nodes);
        if (cycle is not null)
        {
            problems.Add($"The graph has a cycle: {string.Join(" → ", cycle)}.");
        }

        return problems;
    }

    /// <summary>Depth-first search for a cycle. Returns the nodes of the cycle or null.</summary>
    private static List<string>? FindCycle(IReadOnlyList<RoadmapNode> nodes)
    {
        var edges = nodes
            .GroupBy(n => n.Id)
            .ToDictionary(g => g.Key, g => g.First().Prerequisites.Where(p => p != g.Key).ToList());

        var state = new Dictionary<string, int>(); // 0 — not visited, 1 — on the stack, 2 — done
        var stack = new List<string>();

        List<string>? Visit(string id)
        {
            state[id] = 1;
            stack.Add(id);
            foreach (var next in edges.GetValueOrDefault(id, []))
            {
                if (!edges.ContainsKey(next))
                {
                    continue; // non-existent node — already reported above
                }

                var s = state.GetValueOrDefault(next);
                if (s == 1)
                {
                    var start = stack.IndexOf(next);
                    return [.. stack[start..], next];
                }

                if (s == 0 && Visit(next) is { } found)
                {
                    return found;
                }
            }

            stack.RemoveAt(stack.Count - 1);
            state[id] = 2;
            return null;
        }

        foreach (var id in edges.Keys)
        {
            if (state.GetValueOrDefault(id) == 0 && Visit(id) is { } cycle)
            {
                return cycle;
            }
        }

        return null;
    }
}
