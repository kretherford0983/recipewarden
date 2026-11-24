using System.Globalization;
using System.Text.RegularExpressions;

namespace RecipeWarden.Core.Measurement;

/// <summary>
/// BR-122: how one ingredient line is measured. <see cref="Auto"/> follows BR-22 (weight first for precision recipes
/// and dry foods once a scale is accepted); <see cref="Weight"/> and <see cref="AsWritten"/> override it for that line.
/// </summary>
public enum LineMeasure
{
    Auto = 0,
    Weight = 1,
    AsWritten = 2,
}

/// <summary>BR-122, BR-123: per-line measure choices and the small-amount hint.</summary>
public static partial class LineMeasureRules
{
    /// <summary>BR-123: a weight under this many scale steps is coarse on that scale.</summary>
    public const int SmallAmountSteps = 5;

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Spaces();

    /// <summary>The key a choice is kept under: the line's text, trimmed, spaces collapsed, case ignored.</summary>
    public static string Key(string? rawText) =>
        Spaces().Replace(rawText ?? string.Empty, " ").Trim().ToLowerInvariant();

    /// <summary>
    /// BR-122: a line's choice after a save. A choice made in the form wins; otherwise a line whose text is unchanged
    /// keeps its earlier choice; a new or edited line is <see cref="LineMeasure.Auto"/>.
    /// </summary>
    public static LineMeasure Carry(string rawText, IReadOnlyDictionary<string, LineMeasure> previous,
        IReadOnlyDictionary<string, LineMeasure>? chosen)
    {
        ArgumentNullException.ThrowIfNull(previous);
        var key = Key(rawText);
        if (chosen is not null && chosen.TryGetValue(key, out var picked))
        {
            return picked;
        }

        return previous.TryGetValue(key, out var kept) ? kept : LineMeasure.Auto;
    }

    /// <summary>
    /// BR-122: applying a scale suggestion. Picked lines are weighed; offered lines that were not picked stay as
    /// written, so a later inventory change does not offer them again. Indexes outside the offer are ignored.
    /// </summary>
    public static IReadOnlyDictionary<int, LineMeasure> Apply(IReadOnlyCollection<int> offered, IReadOnlyCollection<int>? picked)
    {
        ArgumentNullException.ThrowIfNull(offered);
        var chosen = picked is null ? offered.ToHashSet() : picked.Where(offered.Contains).ToHashSet();
        return offered.ToDictionary(i => i, i => chosen.Contains(i) ? LineMeasure.Weight : LineMeasure.AsWritten);
    }

    /// <summary>"Show me" for chosen lines: "show" (every offered line) or "0,3" (those lines), for this view only.</summary>
    public static IReadOnlySet<int>? ParseShow(string? value, IEnumerable<int>? lines = null)
    {
        var picked = new HashSet<int>();
        foreach (var l in lines ?? [])
        {
            if (l >= 0)
            {
                picked.Add(l);
            }
        }

        if (string.Equals(value, "show", StringComparison.Ordinal))
        {
            return null;
        }

        foreach (var part in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(200))
        {
            if (int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var i))
            {
                picked.Add(i);
            }
        }

        return picked;
    }

    /// <summary>
    /// BR-123: when a weight is under <see cref="SmallAmountSteps"/> steps of the scale, say so and name the spoon
    /// amount. Never blocks the choice.
    /// </summary>
    public static string? SmallAmountHint(decimal grams, decimal stepGrams, string? volume)
    {
        if (stepGrams <= 0m || grams >= stepGrams * SmallAmountSteps)
        {
            return null;
        }

        return "Small amount for this scale: weigh onto a zeroed plate" + (string.IsNullOrEmpty(volume) ? "." : ", or use " + volume + ".");
    }
}
