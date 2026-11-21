using System.Globalization;
using RecipeWarden.Core.Organization;

namespace RecipeWarden.Core.Recipes;

/// <summary>The editable content of a recipe, as text, for comparing two versions (BR-06).</summary>
public sealed record RecipeSnapshot
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public decimal? Servings { get; init; }
    public int? PrepMinutes { get; init; }
    public int? CookMinutes { get; init; }
    public int? TotalMinutes { get; init; }
    public bool IsPrecision { get; init; }
    public string? Notes { get; init; }
    public SourceType SourceType { get; init; }
    public string? SourceName { get; init; }
    public string? SourceUrl { get; init; }
    public string? SourceAuthor { get; init; }
    public IReadOnlyList<string> Ingredients { get; init; } = [];
    public IReadOnlyList<string> Steps { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>One field that differs: what this user submitted and what is saved now.</summary>
public sealed record FieldDifference(RecipeField Field, string Mine, string Theirs);

/// <summary>
/// BR-06: when a save is rejected because someone else saved first, the editor shows this message and the fields
/// that differ between the submitted form and the current saved recipe.
/// </summary>
public static class RecipeDiff
{
    public const string ConflictMessage =
        "This recipe was changed by someone else while you were editing. Review the differences below, then save again.";

    public static IReadOnlyList<FieldDifference> Compare(RecipeSnapshot mine, RecipeSnapshot theirs)
    {
        ArgumentNullException.ThrowIfNull(mine);
        ArgumentNullException.ThrowIfNull(theirs);

        var diffs = new List<FieldDifference>();
        Add(diffs, RecipeField.Title, mine.Title, theirs.Title);
        Add(diffs, RecipeField.Description, mine.Description, theirs.Description);
        Add(diffs, RecipeField.Servings, Number(mine.Servings), Number(theirs.Servings));
        Add(diffs, RecipeField.PrepMinutes, Number(mine.PrepMinutes), Number(theirs.PrepMinutes));
        Add(diffs, RecipeField.CookMinutes, Number(mine.CookMinutes), Number(theirs.CookMinutes));
        Add(diffs, RecipeField.TotalMinutes, Number(mine.TotalMinutes), Number(theirs.TotalMinutes));
        Add(diffs, RecipeField.IsPrecision, YesNo(mine.IsPrecision), YesNo(theirs.IsPrecision));
        Add(diffs, RecipeField.Notes, mine.Notes, theirs.Notes);
        Add(diffs, RecipeField.SourceType, mine.SourceType.ToString(), theirs.SourceType.ToString());
        Add(diffs, RecipeField.SourceName, mine.SourceName, theirs.SourceName);
        Add(diffs, RecipeField.SourceUrl, mine.SourceUrl, theirs.SourceUrl);
        Add(diffs, RecipeField.SourceAuthor, mine.SourceAuthor, theirs.SourceAuthor);
        Add(diffs, RecipeField.Ingredients, Lines(mine.Ingredients), Lines(theirs.Ingredients));
        Add(diffs, RecipeField.Steps, Lines(mine.Steps), Lines(theirs.Steps));

        if (!SameTags(mine.Tags, theirs.Tags))
        {
            diffs.Add(new FieldDifference(RecipeField.Tags, string.Join(", ", mine.Tags), string.Join(", ", theirs.Tags)));
        }

        return diffs;
    }

    private static void Add(List<FieldDifference> diffs, RecipeField field, string? mine, string? theirs)
    {
        var a = mine ?? string.Empty;
        var b = theirs ?? string.Empty;
        if (!string.Equals(a, b, StringComparison.Ordinal))
        {
            diffs.Add(new FieldDifference(field, a, b));
        }
    }

    private static string Number(decimal? value) => value?.ToString("G29", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Number(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string YesNo(bool value) => value ? "Yes" : "No";

    private static string Lines(IReadOnlyList<string> items) => string.Join('\n', items);

    private static bool SameTags(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        a.Select(NameRules.Normalize).Order(StringComparer.Ordinal).Distinct()
            .SequenceEqual(b.Select(NameRules.Normalize).Order(StringComparer.Ordinal).Distinct(), StringComparer.Ordinal);
}
