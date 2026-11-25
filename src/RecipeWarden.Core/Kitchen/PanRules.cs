using System.Globalization;
using System.Text.RegularExpressions;

namespace RecipeWarden.Core.Kitchen;

/// <summary>A pan's shape and size in inches. For round pans <see cref="Width"/> is the diameter.</summary>
public sealed record Pan(PanShape Shape, decimal Width, decimal Length)
{
    public decimal Area => Shape == PanShape.Round ? (decimal)Math.PI * (Width / 2m) * (Width / 2m) : Width * Length;
}

/// <summary>A pan suggestion for a scaled recipe: <see cref="Count"/> of a pan, owned or from the standard list.</summary>
public sealed record PanOption(int Count, Pan Pan, string Name, bool Owned, decimal Error)
{
    public string Text => (Count > 1 ? Count.ToString(CultureInfo.InvariantCulture) + " × " : string.Empty) + Name
        + (Owned ? " (in My Kitchen)" : string.Empty);
}

/// <summary>
/// FR-MEAS-07 (pan part), BR-29, ADR 0020: pan sizes read from text ("9x13", "9-inch round", "8x8 square",
/// "9x5 loaf", "half sheet"), found in step text, and matched by area when a recipe is scaled (±15%, up to 3 pans).
/// </summary>
public static partial class PanRules
{
    public const decimal Tolerance = 0.15m;
    public const int MaxPans = 3;
    public const string TimeNote = "A different pan changes the bake time: check for doneness early.";

    public static IReadOnlyList<(string Name, Pan Pan)> Standard { get; } =
    [
        ("8x8 square", new Pan(PanShape.Square, 8m, 8m)),
        ("9x9 square", new Pan(PanShape.Square, 9m, 9m)),
        ("7x11 rectangle", new Pan(PanShape.Rectangle, 7m, 11m)),
        ("9x13 rectangle", new Pan(PanShape.Rectangle, 9m, 13m)),
        ("half sheet (13x18)", new Pan(PanShape.Rectangle, 13m, 18m)),
        ("8-inch round", new Pan(PanShape.Round, 8m, 8m)),
        ("9-inch round", new Pan(PanShape.Round, 9m, 9m)),
        ("10-inch round", new Pan(PanShape.Round, 10m, 10m)),
        ("9x5 loaf", new Pan(PanShape.Loaf, 5m, 9m)),
        ("8.5x4.5 loaf", new Pan(PanShape.Loaf, 4.5m, 8.5m)),
    ];

    [GeneratedRegex(@"\b(?<w>\d{1,2}(?:\.\d+)?)\s*(?:-?\s*(?:inch(?:es)?|in\.?|""))?\s*(?:x|×|by)\s*(?<l>\d{1,2}(?:\.\d+)?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex TwoSides();

    [GeneratedRegex(@"\b(?<d>\d{1,2}(?:\.\d+)?)\s*(?:-|\s)?\s*(?:inch(?:es)?|in\.?|"")\s*(?<s>round|square|springform|cake|pie|tart)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex OneSide();

    [GeneratedRegex(@"\b(?:\d{1,2}(?:\.\d+)?\s*(?:-?\s*(?:inch(?:es)?|in\.?|""))?\s*(?:x|×|by)\s*\d{1,2}(?:\.\d+)?|\d{1,2}(?:\.\d+)?\s*(?:-|\s)?\s*(?:inch(?:es)?|in\.?|"")(?:\s*(?:round|square|springform|cake|pie|tart))?|half[- ]sheet|quarter[- ]sheet)[^.;\n]{0,25}?\b(?:pan|dish|tin|sheet|skillet)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex PanInSteps();

    public static Pan? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 200)
        {
            return null;
        }

        var lower = text.ToUpperInvariant();
        if (lower.Contains("HALF", StringComparison.Ordinal) && lower.Contains("SHEET", StringComparison.Ordinal))
        {
            return new Pan(PanShape.Rectangle, 13m, 18m);
        }

        if (lower.Contains("QUARTER", StringComparison.Ordinal) && lower.Contains("SHEET", StringComparison.Ordinal))
        {
            return new Pan(PanShape.Rectangle, 9m, 13m);
        }

        if (TwoSides().Match(text) is { Success: true } two)
        {
            var a = decimal.Parse(two.Groups["w"].Value, CultureInfo.InvariantCulture);
            var b = decimal.Parse(two.Groups["l"].Value, CultureInfo.InvariantCulture);
            var (w, l) = (Math.Min(a, b), Math.Max(a, b));
            var shape = lower.Contains("LOAF", StringComparison.Ordinal) ? PanShape.Loaf : w == l ? PanShape.Square : PanShape.Rectangle;
            return Valid(new Pan(shape, w, l));
        }

        if (OneSide().Match(text) is { Success: true } one)
        {
            var d = decimal.Parse(one.Groups["d"].Value, CultureInfo.InvariantCulture);
            var square = lower.Contains("SQUARE", StringComparison.Ordinal);
            return Valid(new Pan(square ? PanShape.Square : PanShape.Round, d, d));
        }

        return null;
    }

    /// <summary>The first pan named in the steps ("Grease a 9x13-inch baking dish"), for prefilling the editor.</summary>
    public static Pan? FromSteps(string? steps)
    {
        if (string.IsNullOrEmpty(steps) || steps.Length > 20_000)
        {
            return null;
        }

        try
        {
            return PanInSteps().Match(steps) is { Success: true } m ? Parse(m.Value) : null;
        }
        catch (RegexMatchTimeoutException)
        {
            // Prefilling the pan is a convenience: a busy host must never fail the recipe page over it.
            return null;
        }
    }

    public static string Describe(Pan pan)
    {
        ArgumentNullException.ThrowIfNull(pan);
        static string N(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        return pan.Shape switch
        {
            PanShape.Round => N(pan.Width) + "-inch round",
            PanShape.Square => N(pan.Width) + "x" + N(pan.Width) + " square",
            PanShape.Loaf => N(pan.Length) + "x" + N(pan.Width) + " loaf",
            _ => N(pan.Width) + "x" + N(pan.Length),
        };
    }

    /// <summary>
    /// Pans for the recipe at <paramref name="factor"/>: owned pans first, then the standard list, each alone or
    /// repeated (up to 3), within ±15% of the scaled area; best first. Empty when the factor is 1.
    /// </summary>
    public static IReadOnlyList<PanOption> Alternatives(Pan pan, decimal factor, IEnumerable<(string Name, Pan Pan)> owned, int max = 4)
    {
        ArgumentNullException.ThrowIfNull(pan);
        ArgumentNullException.ThrowIfNull(owned);
        if (factor == 1m)
        {
            return [];
        }

        var target = pan.Area * factor;
        var ownedList = owned.ToList();
        var candidates = ownedList.Select(o => (o.Name, o.Pan, Owned: true))
            .Concat(Standard.Where(s => !ownedList.Any(o => SameSize(o.Pan, s.Pan))).Select(s => (s.Name, s.Pan, Owned: false)));
        var options = new List<PanOption>();
        foreach (var (name, candidate, isOwned) in candidates)
        {
            for (var count = 1; count <= MaxPans; count++)
            {
                var error = Math.Abs(candidate.Area * count - target) / target;
                if (error <= Tolerance)
                {
                    options.Add(new PanOption(count, candidate, name, isOwned, Math.Round(error, 4)));
                }
            }
        }

        return options.OrderByDescending(o => o.Owned).ThenBy(o => o.Count).ThenBy(o => o.Error).Take(max).ToList();
    }

    private static bool SameSize(Pan a, Pan b) => a.Shape == b.Shape && a.Width == b.Width && a.Length == b.Length;

    private static Pan? Valid(Pan pan) =>
        pan.Width is > 0m and <= KitchenRules.MaxPanInches && pan.Length is > 0m and <= KitchenRules.MaxPanInches ? pan : null;
}
