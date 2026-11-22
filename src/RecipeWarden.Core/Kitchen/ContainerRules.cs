using System.Globalization;
using RecipeWarden.Core.Measurement;

namespace RecipeWarden.Core.Kitchen;

/// <summary>Section 8.2 Recipe.YieldSource. Precedence: measured > entered > estimated (BR-111).</summary>
public enum YieldSource
{
    Estimated = 0,
    Entered = 1,
    Measured = 2,
}

/// <summary>A finished yield in ml (BR-110), where it came from, and what an estimate had to leave out.</summary>
public sealed record FinishedYield(decimal Ml, YieldSource Source, IReadOnlyList<string> Excluded)
{
    public bool IsEstimate => Source == YieldSource.Estimated;
}

/// <summary>The container to fill: capacity in ml, its unit for display, fill %, count and how many are owned.</summary>
public sealed record ContainerChoice(string Noun, decimal CapacityMl, UnitDefinition DisplayUnit, decimal FillPct, int Count, int? Owned);

/// <summary>Result of "Fit to container" (FR-SCL-04/05): the factor and the lines to show, or why it cannot be done.</summary>
public sealed record ContainerFit(decimal? Factor, string? Fill, string? Note, string? Error);

/// <summary>
/// FR-SCL-04, FR-SCL-05, FR-SCL-06, BR-110 to BR-114, ADR 0019: container scaling as a view. Pure arithmetic on
/// ml; the caller supplies the yield (stored or typed) or an estimate from the ingredients.
/// </summary>
public static class ContainerRules
{
    public const string WeightYieldNeedsDensity =
        "This recipe's yield is a weight. Enter the finished product's density (g/ml) in the editor to fill a container by volume.";

    public const string NoYield = "Enter how much the recipe makes, or use the estimate.";
    public const decimal DriftLimit = 0.05m;

    public static IReadOnlyDictionary<string, decimal> DefaultFill { get; } = new Dictionary<string, decimal>(StringComparer.Ordinal)
    {
        ["bottle"] = 90m, ["jar"] = 90m, ["freezer"] = 85m, ["serving"] = 100m,
    };

    /// <summary>
    /// BR-111: an estimate from the ingredients that convert to a volume (volumes as they are; weights with a
    /// density). Everything else is listed as excluded. At 1x.
    /// </summary>
    public static FinishedYield Estimate(IEnumerable<ScaleInput> lines, MeasurementCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(catalog);
        var total = 0m;
        var excluded = new List<string>();
        foreach (var line in lines)
        {
            var p = line.Ingredient;
            var unit = catalog.Unit(p.UnitCode);
            var density = catalog.Food(line.FoodId)?.DensityGPerMl;
            if (p.Quantity is not { } q || p.NeedsReview)
            {
                if (p.Quantity is not null || !string.IsNullOrEmpty(p.UnitCode))
                {
                    excluded.Add(p.Name.Length > 0 ? p.Name : p.RawText);
                }

                continue;
            }

            if (unit is { Kind: UnitKind.Volume } && !unit.IsVague)
            {
                total += q * unit.ToBase;
            }
            else if (unit is { Kind: UnitKind.Mass } && density is > 0m)
            {
                total += q * unit.ToBase / density.Value;
            }
            else if (unit is { IsVague: true })
            {
                total += q * unit.ToBase; // a pinch adds next to nothing, but it is not missing either
            }
            else
            {
                excluded.Add(p.Name.Length > 0 ? p.Name : p.RawText);
            }
        }

        return new FinishedYield(total, YieldSource.Estimated, excluded);
    }

    /// <summary>
    /// The stored yield as ml (BR-110), or null. A weight needs <paramref name="finishedDensity"/> (BR-112);
    /// <paramref name="error"/> says why not.
    /// </summary>
    public static FinishedYield? Stored(decimal? amount, string? unitCode, YieldSource? source, decimal? finishedDensity,
        out string? error)
    {
        error = null;
        if (amount is not > 0m || UnitCatalog.FindByCode(unitCode) is not { } unit || source is null)
        {
            return null;
        }

        if (unit.Kind == UnitKind.Volume)
        {
            return new FinishedYield(amount.Value * unit.ToBase, source.Value, []);
        }

        if (unit.Kind == UnitKind.Mass)
        {
            if (finishedDensity is > 0m)
            {
                return new FinishedYield(amount.Value * unit.ToBase / finishedDensity.Value, source.Value, []);
            }

            error = WeightYieldNeedsDensity;
        }

        return null;
    }

    /// <summary>
    /// BR-110: factor = capacity × fill % × count ÷ yield. Shows "fills about 14.4 of 16 fl oz" (BR-113 checks the
    /// rounding separately) and notes when more containers are asked for than owned (BR-114).
    /// </summary>
    public static ContainerFit Fit(FinishedYield yield, ContainerChoice container)
    {
        ArgumentNullException.ThrowIfNull(yield);
        ArgumentNullException.ThrowIfNull(container);
        if (yield.Ml <= 0m)
        {
            return new ContainerFit(null, null, null, NoYield);
        }

        if (container.CapacityMl <= 0m || container.FillPct is <= 0m or > 100m || container.Count < 1)
        {
            return new ContainerFit(null, null, null, "Enter a capacity, a fill % between 1 and 100, and how many containers.");
        }

        var perContainer = container.CapacityMl * container.FillPct / 100m;
        var factor = Math.Round(perContainer * container.Count / yield.Ml, 4);
        if (!ScaleRules.InRange(factor))
        {
            return new ContainerFit(null, null, null, ScaleRules.OutOfRange);
        }

        var unit = container.DisplayUnit;
        var fill = "fills about " + Amount(perContainer / unit.ToBase) + " of " + Amount(container.CapacityMl / unit.ToBase) + " "
                   + unit.Name + (container.Count > 1 ? " in each of " + container.Count.ToString(CultureInfo.InvariantCulture) + " " + Plural(container.Noun) : string.Empty);
        string? note = null;
        if (container.Owned is { } owned && container.Count > owned)
        {
            note = "You have " + owned.ToString(CultureInfo.InvariantCulture) + "; this needs " + container.Count.ToString(CultureInfo.InvariantCulture) + ".";
        }

        return new ContainerFit(factor, fill, note, null);
    }

    /// <summary>FR-SCL-05: "1 full bottle + about ⅔ of another" for the yield at the current factor.</summary>
    public static string HowMany(decimal yieldMlAtFactor, ContainerChoice container)
    {
        ArgumentNullException.ThrowIfNull(container);
        var per = container.CapacityMl * container.FillPct / 100m;
        if (per <= 0m || yieldMlAtFactor <= 0m)
        {
            return string.Empty;
        }

        var n = yieldMlAtFactor / per;
        var whole = decimal.Floor(n);
        var rest = n - whole;
        decimal part = 0m;
        if (rest > 0.875m)
        {
            whole++;
        }
        else if (rest >= 0.125m)
        {
            part = Fractions.Nearest(rest, [2, 3, 4]).Value;
        }

        var noun = container.Noun;
        if (whole == 0m)
        {
            return "about " + Fractions.Format(part) + " of a " + noun;
        }

        var text = Fractions.Format(whole) + " full " + (whole == 1m ? noun : Plural(noun));
        return part > 0m ? text + " + about " + Fractions.Format(part) + " of another" : text;
    }

    /// <summary>
    /// BR-113: after rounding, how far the shown amounts are from the exact ones; a warning above 5%
    /// ("rounding puts you about 7% over: may overflow").
    /// </summary>
    public static string? RoundingWarning(IEnumerable<ScaledIngredient> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var exact = 0m;
        var shown = 0m;
        foreach (var l in lines)
        {
            if (l.ExactMl is { } e && l.ShownMl is { } s)
            {
                exact += e;
                shown += s;
            }
        }

        if (exact <= 0m)
        {
            return null;
        }

        var drift = (shown - exact) / exact;
        if (Math.Abs(drift) <= DriftLimit)
        {
            return null;
        }

        var pct = Math.Round(Math.Abs(drift) * 100m).ToString("0", CultureInfo.InvariantCulture);
        return drift > 0m
            ? "Rounding the amounts puts you about " + pct + "% over: the containers may overflow."
            : "Rounding the amounts puts you about " + pct + "% under: the containers will not be full.";
    }

    /// <summary>The noun for a container name: "16 oz squeeze bottle" → "bottle".</summary>
    public static string Noun(string? name)
    {
        var words = (name ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? "container" : FoodMatcher.Singular(words[^1].ToLowerInvariant());
    }

    private static string Plural(string noun) => FoodMatcher.Plural(noun);

    private static string Amount(decimal v) => Math.Round(v, 1).ToString("0.#", CultureInfo.InvariantCulture);
}
