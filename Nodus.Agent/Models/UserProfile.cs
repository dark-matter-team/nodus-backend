namespace Nodus.Agent.Models;

/// <summary>
/// What the user wants and what they already know. Comes from the questionnaire in the app.
/// </summary>
public sealed record UserProfile
{
    /// <summary>The goal, e.g. "Data Engineer".</summary>
    public required string Goal { get; init; }

    /// <summary>Skills with a self-assessed level.</summary>
    public IReadOnlyList<SkillLevel> Skills { get; init; } = [];

    /// <summary>Free text about experience: "2 years of C# backend development".</summary>
    public string? Background { get; init; }

    public int HoursPerWeek { get; init; } = 10;

    /// <summary>Desired deadline in weeks (optional).</summary>
    public int? DeadlineWeeks { get; init; }

    /// <summary>Language of the roadmap and preferred materials: "en" or "uk". See <see cref="SupportedLanguages"/>.</summary>
    public string Language { get; init; } = "en";

    public bool FreeResourcesOnly { get; init; }
}

/// <summary>
/// Level: 0 — none, 1 — basic, 2 — confident, 3 — fluent.
/// </summary>
public sealed record SkillLevel(string Name, int Level);
