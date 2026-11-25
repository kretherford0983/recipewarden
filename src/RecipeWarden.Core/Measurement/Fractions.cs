using System.Globalization;

namespace RecipeWarden.Core.Measurement;

/// <summary>A value rounded to a kitchen fraction, with the relative error of the rounding.</summary>
public readonly record struct RoundedAmount(decimal Value, int Denominator, decimal Error);

/// <summary>
/// BR-28: amounts show as fractions with denominators 2, 3, 4 and 8 (and 16 for teaspoons); metric amounts show as
/// decimals. Fractions use the same glyphs as UX-05 (½, ⅓, ⅝) where one exists.
/// </summary>
public static class Fractions
{
    /// <summary>BR-28 denominators for US amounts.</summary>
    public static IReadOnlyList<int> Standard { get; } = [2, 3, 4, 8];

    /// <summary>BR-28 denominators for teaspoons.</summary>
    public static IReadOnlyList<int> Teaspoon { get; } = [2, 3, 4, 8, 16];

    private static readonly Dictionary<(int Num, int Den), string> Glyphs = new()
    {
        [(1, 2)] = "½", [(1, 3)] = "⅓", [(2, 3)] = "⅔", [(1, 4)] = "¼", [(3, 4)] = "¾",
        [(1, 8)] = "⅛", [(3, 8)] = "⅜", [(5, 8)] = "⅝", [(7, 8)] = "⅞",
    };

    /// <summary>
    /// The nearest value whose fractional part has one of <paramref name="denominators"/> (or is whole). Ties go to
    /// the smaller denominator. A positive value never rounds to zero: the smallest fraction is used instead.
    /// </summary>
    public static RoundedAmount Nearest(decimal value, IReadOnlyList<int> denominators)
    {
        ArgumentNullException.ThrowIfNull(denominators);
        if (value <= 0m)
        {
            return new RoundedAmount(0m, 1, 0m);
        }

        var best = new RoundedAmount(Math.Round(value, MidpointRounding.AwayFromZero), 1, decimal.MaxValue);
        best = best with { Error = RelativeError(value, best.Value) };
        foreach (var d in denominators)
        {
            var candidate = Math.Round(value * d, MidpointRounding.AwayFromZero) / d;
            var error = RelativeError(value, candidate);
            if (candidate > 0m && error < best.Error)
            {
                best = new RoundedAmount(candidate, d, error);
            }
        }

        if (best.Value == 0m)
        {
            var smallest = denominators.Count == 0 ? 1 : denominators.Max();
            var v = 1m / smallest;
            best = new RoundedAmount(v, smallest, RelativeError(value, v));
        }

        return best;
    }

    public static decimal RelativeError(decimal exact, decimal rounded) =>
        exact == 0m ? (rounded == 0m ? 0m : 1m) : Math.Abs(rounded - exact) / exact;

    /// <summary>"1½", "⅝", "1/16", "2 3/16", "3".</summary>
    public static string Format(decimal value)
    {
        var whole = decimal.Truncate(value);
        var fraction = value - whole;
        if (fraction == 0m)
        {
            return whole.ToString(CultureInfo.InvariantCulture);
        }

        foreach (var den in new[] { 2, 3, 4, 8, 16 })
        {
            var num = fraction * den;
            if (Math.Abs(num - Math.Round(num)) < 0.0001m)
            {
                var n = (int)Math.Round(num);
                var text = Glyphs.TryGetValue((n, den), out var glyph)
                    ? glyph
                    : n.ToString(CultureInfo.InvariantCulture) + "/" + den.ToString(CultureInfo.InvariantCulture);
                if (whole == 0m)
                {
                    return text;
                }

                return whole.ToString(CultureInfo.InvariantCulture) + (glyph is null ? " " : string.Empty) + text;
            }
        }

        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>BR-28 metric decimals: grams 1 decimal under 10 g, whole otherwise; ml whole; l and kg up to 2 decimals.</summary>
    public static string FormatMetric(decimal value, string unitCode)
    {
        var rounded = unitCode switch
        {
            "g" => value < 10m ? Math.Round(value, 1, MidpointRounding.AwayFromZero) : Math.Round(value, 0, MidpointRounding.AwayFromZero),
            "ml" => Math.Round(value, value < 1m ? 1 : 0, MidpointRounding.AwayFromZero),
            _ => Math.Round(value, 2, MidpointRounding.AwayFromZero),
        };
        if (rounded == 0m && value > 0m)
        {
            rounded = 0.1m;
        }

        return rounded.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
