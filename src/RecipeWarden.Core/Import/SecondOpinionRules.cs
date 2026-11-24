using System.Globalization;
using System.Text;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Python;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Import;

/// <summary>What the two parsers disagree on (BR-118). Preparation and notes are never compared.</summary>
[Flags]
public enum ReadingDifference
{
    None = 0,
    Quantity = 1,
    Unit = 2,
    Food = 4,
}

/// <summary>A disagreement to show on the review page: what differs and the other parser's reading as text.</summary>
public sealed record SecondOpinion(ReadingDifference Differences, string OtherReading)
{
    public string Summary
    {
        get
        {
            var parts = new List<string>(3);
            if (Differences.HasFlag(ReadingDifference.Quantity))
            {
                parts.Add("amount");
            }

            if (Differences.HasFlag(ReadingDifference.Unit))
            {
                parts.Add("unit");
            }

            if (Differences.HasFlag(ReadingDifference.Food))
            {
                parts.Add("ingredient");
            }

            return "A second reading differs on the " + string.Join(" and ", parts) + ": " + OtherReading;
        }
    }
}

/// <summary>
/// BR-118, FR-IMP-11, D4: compares the .NET parse (the parser of record) with the NLP parser's reading. Quantities are
/// equal within 0.001 (a range compares both ends), units after the unit alias table, food names after dropping the
/// FoodMatcher modifier words and plurals. Pure; the reading is untrusted text (SEC-93) and only ever shown as text.
/// </summary>
public static class SecondOpinionRules
{
    public const decimal QuantityTolerance = 0.001m;
    private const int MaxReadingLength = 300;

    public static SecondOpinion? Compare(ParsedIngredient ours, IngredientReading? theirs)
    {
        ArgumentNullException.ThrowIfNull(ours);
        if (theirs is null)
        {
            return null;
        }

        var differences = ReadingDifference.None;
        if (!Same(ours.Quantity, theirs.Quantity) || !Same(ours.QuantityMax, theirs.QuantityMax))
        {
            differences |= ReadingDifference.Quantity;
        }

        if (CountWordInName(ours, theirs))
        {
            // "6 garlic cloves" vs "6 cloves garlic": the same thing written two ways, not a difference.
        }
        else
        {
            if (!SameUnit(ours.UnitCode, theirs.Unit))
            {
                differences |= ReadingDifference.Unit;
            }

            if (theirs.Names.Count > 0 && !SameFood(ours.Name, theirs.Names))
            {
                differences |= ReadingDifference.Food;
            }
        }

        return differences == ReadingDifference.None ? null : new SecondOpinion(differences, Describe(theirs));
    }

    private static bool Same(decimal? a, decimal? b) =>
        (a, b) switch
        {
            (null, null) => true,
            ({ } x, { } y) => Math.Abs(x - y) <= QuantityTolerance,
            _ => false,
        };

    private static bool SameUnit(string? ourCode, string? theirUnit)
    {
        if (string.IsNullOrWhiteSpace(theirUnit))
        {
            return ourCode is null;
        }

        var theirs = UnitCatalog.FindByAlias(theirUnit.Trim()) ?? UnitCatalog.FindByAlias(theirUnit.Trim().ToLowerInvariant());
        return theirs is not null && string.Equals(theirs.Code, ourCode, StringComparison.Ordinal);
    }

    private static bool SameFood(string ours, IReadOnlyList<string> theirs)
    {
        var core = CoreName(ours);
        return core.Length == 0
            || string.Equals(core, CoreName(string.Join(" and ", theirs)), StringComparison.Ordinal)
            || (theirs.Count == 1 && string.Equals(core, CoreName(theirs[0]), StringComparison.Ordinal));
    }

    /// <summary>
    /// The name without size, state and preparation words, singular, with "or" read as "and" (the NLP parser lists
    /// alternatives as separate names): "large Eggs" → "egg", "butter or margarine" → "butter and margarine".
    /// </summary>
    public static string CoreName(string? name)
    {
        var words = FoodMatcher.Normalize(name).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !FoodMatcher.IsModifier(w))
            .Select(w => w == "or" ? "and" : w);
        return FoodMatcher.Singular(string.Join(' ', words));
    }

    /// <summary>
    /// True when the .NET parser kept a count word in the name ("garlic cloves", no unit) and the NLP parser made it the
    /// unit ("cloves" of "garlic"): the same words, so no unit or food difference.
    /// </summary>
    private static bool CountWordInName(ParsedIngredient ours, IngredientReading theirs)
    {
        if (ours.UnitCode is not null || string.IsNullOrWhiteSpace(theirs.Unit))
        {
            return false;
        }

        static HashSet<string> Words(string text) =>
            [.. FoodMatcher.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !FoodMatcher.IsModifier(w)).Select(FoodMatcher.Singular)];

        var mine = Words(ours.Name);
        return mine.Count > 0 && mine.SetEquals(Words(theirs.Unit + " " + string.Join(" ", theirs.Names)));
    }

    /// <summary>"½–1 cup milk": the other reading as plain text, capped.</summary>
    public static string Describe(IngredientReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        var sb = new StringBuilder();
        if (reading.Quantity is { } q)
        {
            sb.Append(Amount(q));
            if (reading.QuantityMax is { } max)
            {
                sb.Append('–').Append(Amount(max));
            }
        }

        if (!string.IsNullOrWhiteSpace(reading.Unit))
        {
            sb.Append(sb.Length > 0 ? " " : string.Empty).Append(reading.Unit.Trim());
        }

        var names = string.Join(" and ", reading.Names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()));
        if (names.Length > 0)
        {
            sb.Append(sb.Length > 0 ? " " : string.Empty).Append(names);
        }

        var text = sb.Length == 0 ? "(nothing recognised)" : sb.ToString();
        return text.Length > MaxReadingLength ? text[..MaxReadingLength] : text;
    }

    private static string Amount(decimal value)
    {
        var nearest = Fractions.Nearest(value, [2, 3, 4, 8]);
        return Math.Abs(nearest.Value - value) <= QuantityTolerance
            ? Fractions.Format(nearest.Value)
            : value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
