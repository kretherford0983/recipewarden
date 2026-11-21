using System.Globalization;
using System.Text.RegularExpressions;

namespace RecipeWarden.Core.Recipes;

/// <summary>Outcome of <see cref="ServingsRules.Resolve"/>.</summary>
public sealed record ServingsResult(bool IsValid, decimal Servings, bool Estimated, string? Error);

/// <summary>Servings typed in the editor: a number, a range, or nothing (unknown).</summary>
public sealed record ServingsText(decimal? Min, decimal? Max, string? Error);

/// <summary>
/// BR-03: servings must be positive; unknown servings default to 4 and are flagged "servings estimated".
/// ADR 0010: servings may be a range ("6-8"); BR-03 applies to both ends and the high end must be above the low.
/// </summary>
public static partial class ServingsRules
{
    public const decimal Default = 4m;
    public const decimal Maximum = 1000m;
    public const string NotPositive = "Servings must be a positive number";
    public const string TooMany = "Servings must be 1000 or fewer";
    public const string Unreadable = "Enter servings as a number or a range, like 6 or 6-8";
    public const string HighBelowLow = "The second number of a range must be larger than the first";
    public const string HighWithoutLow = "A range needs a first number";

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)(?:\s*(?:-|–|—|to)\s*(\d+(?:\.\d+)?))?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex Pattern();

    [GeneratedRegex(@"\b(?:serves|serving|servings|makes|people|persons|portions|about|approx\.?|approximately)\b|~", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex Words();

    public static ServingsResult Resolve(decimal? servings) => servings switch
    {
        null => new ServingsResult(true, Default, true, null),
        <= 0m => new ServingsResult(false, 0m, false, NotPositive),
        > Maximum => new ServingsResult(false, 0m, false, TooMany),
        _ => new ServingsResult(true, servings.Value, false, null),
    };

    /// <summary>Reads "6", "6-8", "6 to 8", "Serves 6–8". Blank gives no value and no error (BR-03 default applies).</summary>
    public static ServingsText ParseText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ServingsText(null, null, null);
        }

        var cleaned = Words().Replace(text, " ").Trim();
        var m = Pattern().Match(cleaned);
        if (!m.Success
            || !decimal.TryParse(m.Groups[1].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var min))
        {
            return new ServingsText(null, null, Unreadable);
        }

        decimal? max = null;
        if (m.Groups[2].Success)
        {
            if (!decimal.TryParse(m.Groups[2].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var high))
            {
                return new ServingsText(null, null, Unreadable);
            }

            max = high == min ? null : high;
        }

        var error = ValidateRange(min, max);
        return error is null ? new ServingsText(min, max, null) : new ServingsText(null, null, error);
    }

    /// <summary>BR-03 for both ends of a stored range.</summary>
    public static string? ValidateRange(decimal? min, decimal? max)
    {
        if (min is null)
        {
            return max is null ? null : HighWithoutLow;
        }

        if (Resolve(min).Error is { } lowError)
        {
            return lowError;
        }

        if (max is null)
        {
            return null;
        }

        if (max > Maximum)
        {
            return TooMany;
        }

        return max <= min ? HighBelowLow : null;
    }

    /// <summary>"6", "6–8", "2½" (UX-05).</summary>
    public static string Format(decimal min, decimal? max) =>
        max is { } high ? IngredientFormatter.Amount(min) + "–" + IngredientFormatter.Amount(high) : IngredientFormatter.Amount(min);
}
