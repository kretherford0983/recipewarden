using System.Globalization;

namespace RecipeWarden.Core.Measurement;

/// <summary>The scale a reader asked for: the factor, the target servings if they gave one, and why it was refused.</summary>
public sealed record ScaleChoice(decimal Factor, decimal? TargetServings, string? Error)
{
    public bool IsScaled => Factor != 1m;
}

/// <summary>
/// FR-SCL-01, FR-SCL-03, BR-20, BR-29, ADR 0014: scale presets, a custom factor (0.1x–100x) and target servings,
/// read from the query string. Scaling is a view only; nothing here changes a stored recipe.
/// </summary>
public static class ScaleRules
{
    public const decimal MinFactor = 0.1m;
    public const decimal MaxFactor = 100m;
    public const int MaxTextLength = 20;

    public const string OutOfRange = "The scale must be between 0.1x and 100x; showing the recipe as written.";
    public const string NotANumber = "The scale must be a number such as 1.5 or 1/2; showing the recipe as written.";
    public const string ServingsNotANumber = "Servings must be a number such as 6; showing the recipe as written.";

    /// <summary>BR-29: shown when a recipe is scaled above 2x.</summary>
    public const string TimeNote = "Cook times may change when a recipe is scaled above 2x; check for doneness.";

    /// <summary>FR-SCL-01 presets.</summary>
    public static IReadOnlyList<decimal> Presets { get; } = [0.5m, 1m, 1.5m, 2m, 3m];

    /// <summary>UX-13: the presets on the collapsed recipe toolbar (owner feedback 2026-11-23).</summary>
    public static IReadOnlyList<decimal> QuickPresets { get; } = [0.5m, 1m, 2m, 3m];

    /// <summary>UX-13: the other presets, under the toolbar's "More scaling options".</summary>
    public static IReadOnlyList<decimal> OtherPresets { get; } = [.. Presets.Except(QuickPresets)];

    /// <summary>
    /// Reads <c>?scale=</c> or <c>?servings=</c> (servings wins when both are given). Anything that cannot be used
    /// falls back to 1x with a message, never an error page (ADR 0014).
    /// </summary>
    public static ScaleChoice Parse(string? scale, string? servings, decimal recipeServings)
    {
        if (!string.IsNullOrWhiteSpace(servings))
        {
            if (!TryParseNumber(servings, out var target) || target <= 0m || recipeServings <= 0m)
            {
                return new ScaleChoice(1m, null, ServingsNotANumber);
            }

            var factor = target / recipeServings;
            return InRange(factor) ? new ScaleChoice(factor, target, null) : new ScaleChoice(1m, null, OutOfRange);
        }

        if (string.IsNullOrWhiteSpace(scale))
        {
            return new ScaleChoice(1m, null, null);
        }

        var text = scale.Trim().TrimEnd('x', 'X', '×').Trim();
        if (!TryParseNumber(text, out var value))
        {
            return new ScaleChoice(1m, null, NotANumber);
        }

        return InRange(value) ? new ScaleChoice(value, null, null) : new ScaleChoice(1m, null, OutOfRange);
    }

    public static bool InRange(decimal factor) => factor >= MinFactor && factor <= MaxFactor;

    /// <summary>BR-20: servings (and the high end of a range) multiplied by the factor, to 2 decimals.</summary>
    public static (decimal Servings, decimal? ServingsMax) Servings(decimal servings, decimal? servingsMax, decimal factor) =>
        (Math.Round(servings * factor, 2, MidpointRounding.AwayFromZero),
         servingsMax is { } max ? Math.Round(max * factor, 2, MidpointRounding.AwayFromZero) : null);

    /// <summary>BR-29: a note when the factor is above 2x.</summary>
    public static string? NoteFor(decimal factor) => factor > 2m ? TimeNote : null;

    /// <summary>"1.5x", "0.5x", "10x"; long decimals shortened ("0.67x").</summary>
    public static string Label(decimal factor) => Math.Round(factor, 2).ToString("0.##", CultureInfo.InvariantCulture) + "x";

    /// <summary>The value for <c>?scale=</c>: up to 4 decimals, no trailing zeros.</summary>
    public static string ToQuery(decimal factor) => Math.Round(factor, 4).ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>Accepts "1.5", "2", "1/2", "1 1/2". Length-limited so query strings cannot be expensive to read.</summary>
    public static bool TryParseNumber(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength)
        {
            return false;
        }

        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 2)
        {
            return false;
        }

        decimal total = 0m;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var slash = part.IndexOf('/', StringComparison.Ordinal);
            if (slash > 0)
            {
                if (!decimal.TryParse(part[..slash], NumberStyles.None, CultureInfo.InvariantCulture, out var num)
                    || !decimal.TryParse(part[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var den)
                    || den == 0m)
                {
                    return false;
                }

                total += num / den;
            }
            else if (i == 0 && decimal.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var whole))
            {
                total += whole;
            }
            else
            {
                return false;
            }
        }

        value = total;
        return true;
    }
}
