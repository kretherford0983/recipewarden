using System.Globalization;
using System.Text;

namespace RecipeWarden.Core.AiExchange;

/// <summary>Section 4.9 goal catalogue (built in, ADR 0033).</summary>
public enum AiGoalKind
{
    ImproveFlavour,
    ReduceFlavour,
    IncreaseComponent,
    FixTexture,
    Healthier,
    AvoidIngredient,
    Cheaper,
    AdaptToTools,
    MakeAhead,
    ClarifySteps,
    Custom,
}

/// <summary>A chosen goal and its details: <see cref="Target"/> (aspect, ingredient, component, problem…) and <see cref="Amount"/>.</summary>
public sealed record AiGoal(AiGoalKind Kind, string? Target = null, string? Amount = null);

/// <summary>Everything a prompt is built from (FR-EXP-04, FR-EXP-07). Recipes are recipe-md v1 text.</summary>
public sealed record AiPromptRequest
{
    public IReadOnlyList<string> Titles { get; init; } = [];
    public IReadOnlyList<AiGoal> Goals { get; init; } = [];
    public string? LastTime { get; init; }
    public bool KeepServings { get; init; }
    public decimal? Servings { get; init; }
    public int? MaxTotalMinutes { get; init; }
    public string? MustKeep { get; init; }
    public bool AskQuestions { get; init; }

    /// <summary>BR-96: the user confirmed conflicting goals.</summary>
    public bool KeepConflicts { get; init; }

    /// <summary>"Include My Kitchen": tool lines and the unit system (BR-91), or null.</summary>
    public IReadOnlyList<string>? Kitchen { get; init; }
    public string? Units { get; init; }
}

/// <summary>
/// FR-EXP-04, FR-EXP-07, BR-91, BR-94 to BR-97, ADR 0033: builds the prompt for an AI assistant. The text is all the
/// app produces; it never calls an AI service (BR-90). Typed text is quoted on one line and never becomes instructions.
/// </summary>
public static class AiPromptBuilder
{
    public const int MaxFreeText = 500;
    public const string ReplyContract =
        "When we're done, reply with the complete revised recipe in recipe-md v1 (same front matter fields, source block "
        + "unchanged) followed by a \"## Changes\" section listing each change and why.";

    public const string GeneralGoal = "Review the recipe and suggest improvements to flavour, clarity and technique, keeping the dish's character.";

    public static IReadOnlyList<(AiGoalKind Kind, string Label, string? TargetLabel, IReadOnlyList<string> Amounts)> Catalogue { get; } =
    [
        (AiGoalKind.ImproveFlavour, "Improve flavour", "Aspect (e.g. depth, brightness, heat)", []),
        (AiGoalKind.ReduceFlavour, "Reduce a flavour or ingredient", "Ingredient or flavour", ["a little", "noticeably", "a lot"]),
        (AiGoalKind.IncreaseComponent, "Increase a component", "Component (filling, sauce, topping…)", ["by 25%", "by 50%", "double"]),
        (AiGoalKind.FixTexture, "Fix a texture problem", "Problem", ["too dry", "too wet", "too dense", "too runny", "too tough", "soggy"]),
        (AiGoalKind.Healthier, "Healthier", "Target", ["lower sodium", "lower sugar", "lower fat", "fewer calories", "more protein", "more fibre"]),
        (AiGoalKind.AvoidIngredient, "Avoid an allergen or ingredient", "Allergen or ingredient", []),
        (AiGoalKind.Cheaper, "Cheaper or pantry-friendly", "Ingredients to use up (optional)", []),
        (AiGoalKind.AdaptToTools, "Adapt to my tools", null, []),
        (AiGoalKind.MakeAhead, "Make-ahead or freezer", "How", ["freeze", "refrigerate", "batch"]),
        (AiGoalKind.ClarifySteps, "Clarify the steps", null, []),
        (AiGoalKind.Custom, "Custom", "Your request", []),
    ];

    public static string Label(AiGoalKind kind) => Catalogue.First(c => c.Kind == kind).Label;

    /// <summary>BR-96: goals that pull the same ingredient both ways.</summary>
    public static IReadOnlyList<string> Conflicts(IEnumerable<AiGoal> goals)
    {
        ArgumentNullException.ThrowIfNull(goals);
        var list = goals.ToList();
        var reduce = list.Where(g => g.Kind == AiGoalKind.ReduceFlavour && !string.IsNullOrWhiteSpace(g.Target))
            .Select(g => Clean(g.Target)!).ToList();
        return list.Where(g => g.Kind == AiGoalKind.IncreaseComponent && !string.IsNullOrWhiteSpace(g.Target))
            .Select(g => Clean(g.Target)!)
            .Where(t => reduce.Contains(t, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(t => $"You asked to both reduce and increase “{t}”. Choose one, or tick “Keep both”.")
            .ToList();
    }

    /// <summary>The prompt, or null when goals conflict and the user has not confirmed them (BR-96).</summary>
    public static string? Build(AiPromptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var conflicts = Conflicts(request.Goals);
        if (conflicts.Count > 0 && !request.KeepConflicts)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.Append(request.Titles.Count switch
        {
            0 => "I'm working on my recipes",
            1 => "I'm working on my recipe " + Quote(request.Titles[0]),
            _ => "I'm working on " + request.Titles.Count.ToString(CultureInfo.InvariantCulture) + " of my recipes",
        });
        sb.Append(" (attached below in recipe-md v1 format).\n\n");

        var goals = request.Goals.Where(g => Instruction(g) is not null).ToList();
        if (goals.Count == 0)
        {
            sb.Append("What I want: ").Append(GeneralGoal).Append('\n');
        }
        else
        {
            sb.Append("What I want to change:\n");
            for (var i = 0; i < goals.Count; i++)
            {
                sb.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(". ").Append(Instruction(goals[i])).Append('\n');
            }

            if (conflicts.Count > 0)
            {
                sb.Append("Note: some of these pull in different directions; I want both, so balance them.\n");
            }
        }

        if (Clean(request.LastTime) is { } last)
        {
            sb.Append("\nLast time: ").Append(Quote(last)).Append('\n');
        }

        var constraints = new List<string>();
        if (request.KeepServings)
        {
            constraints.Add(request.Servings is { } s
                ? "keep it to " + s.ToString("0.##", CultureInfo.InvariantCulture) + " servings"
                : "keep the number of servings");
        }

        if (request.MaxTotalMinutes is { } max and > 0)
        {
            constraints.Add("at most " + max.ToString(CultureInfo.InvariantCulture) + " minutes in total");
        }

        if (Clean(request.MustKeep) is { } keep)
        {
            constraints.Add("must keep " + Quote(keep));
        }

        if (constraints.Count > 0)
        {
            sb.Append("Constraints: ").Append(string.Join("; ", constraints)).Append(".\n");
        }

        if (request.Kitchen is { Count: > 0 } kitchen)
        {
            sb.Append("\nMy kitchen has: ").Append(string.Join(", ", kitchen.Select(k => Clean(k)).Where(k => k is not null))).Append('.');
            if (Clean(request.Units) is { } units)
            {
                sb.Append(" I measure in ").Append(units).Append('.');
            }

            sb.Append('\n');
        }

        if (request.AskQuestions)
        {
            sb.Append("\nBefore rewriting, ask me any clarifying questions you need.\n");
        }

        sb.Append('\n').Append(ReplyContract).Append('\n');
        return sb.ToString();
    }

    /// <summary>FR-EXP-04: the header that explains the format, placed before the recipe file(s).</summary>
    public const string FormatHeader =
        "<!-- recipe-md v1: each recipe starts with YAML front matter between --- lines (format: recipe-md/1, title, slug, "
        + "servings, times in minutes, tags, source), then \"# Title\", \"## Ingredients\" (one \"- \" line each, \"### \" for "
        + "groups), \"## Steps\" (numbered), optional \"## Notes\" and \"## Source\". Keep the slug and the source block as "
        + "they are. Schema: /docs/recipe-md.schema.json -->";

    /// <summary>The text to copy: the prompt, then (optionally) the format header and the recipe file(s).</summary>
    public static string Compose(string prompt, IEnumerable<string> recipesMarkdown, bool includeRecipes)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(recipesMarkdown);
        if (!includeRecipes)
        {
            return prompt;
        }

        return prompt + "\n" + FormatHeader + "\n\n" + string.Join("\n", recipesMarkdown);
    }

    private static string? Instruction(AiGoal goal)
    {
        var target = Clean(goal.Target);
        var amount = Clean(goal.Amount);
        return goal.Kind switch
        {
            AiGoalKind.ImproveFlavour => "Suggest changes to improve " + (target is null ? "the flavour" : Quote(target))
                + " without changing the dish's character.",
            AiGoalKind.ReduceFlavour when target is not null => Quote(target) + " is too strong; reduce it "
                + (amount ?? "a little") + " and rebalance anything that depended on it.",
            AiGoalKind.IncreaseComponent when target is not null => "The " + Quote(target) + " needs to be "
                + (amount ?? "increased") + "; scale that part and adjust anything it affects (seasoning, binding, bake time, pan size).",
            AiGoalKind.FixTexture => "Last time it came out " + (target ?? amount ?? "wrong") + "; diagnose likely causes and fix them.",
            AiGoalKind.Healthier => "Make it " + (target ?? amount ?? "healthier") + " while keeping it satisfying; show nutrition-relevant swaps.",
            AiGoalKind.AvoidIngredient when target is not null => "Make it free of " + Quote(target) + " with tested substitutions.",
            AiGoalKind.Cheaper => target is null ? "Reduce the cost." : "Reduce the cost and use up " + Quote(target) + ".",
            AiGoalKind.AdaptToTools => "Adapt the method to the equipment listed.",
            AiGoalKind.MakeAhead => "Restructure for make-ahead" + ((amount ?? target) is { } how ? " (" + how + ")" : string.Empty)
                + ", with storage and reheating steps.",
            AiGoalKind.ClarifySteps => "Rewrite unclear steps, add doneness cues, keep amounts.",
            AiGoalKind.Custom when target is not null => Quote(target),
            _ => null,
        };
    }

    /// <summary>BR-97: typed text in double quotes, on one line, with double quotes inside changed to single ones.</summary>
    public static string Quote(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return "\"" + (Clean(text) ?? string.Empty).Replace('"', '\'').Replace('“', '\'').Replace('”', '\'') + "\"";
    }

    /// <summary>One line, control characters removed, trimmed and capped; null when empty.</summary>
    public static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var sb = new StringBuilder(Math.Min(text.Length, MaxFreeText));
        var space = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                space = sb.Length > 0;
                continue;
            }

            if (space)
            {
                sb.Append(' ');
                space = false;
            }

            sb.Append(c);
            if (sb.Length >= MaxFreeText)
            {
                break;
            }
        }

        return sb.Length == 0 ? null : sb.ToString();
    }
}
