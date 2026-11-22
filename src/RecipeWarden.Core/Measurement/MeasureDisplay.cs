namespace RecipeWarden.Core.Measurement;

/// <summary>An amount ready to show: the rounded value in its unit, and the text ("1⅛ cups", "250 g").</summary>
public sealed record DisplayAmount(decimal Value, UnitDefinition Unit, string Text, decimal Error);

/// <summary>
/// FR-MEAS-01/02 (M2 part, ADR 0012), BR-24, BR-28: picks the unit an amount shows in and rounds it, without the
/// kitchen inventory. The display planner (M3) will replace the unit choice with tool combinations; this is its
/// "nearest fraction" fallback (9.4).
/// <list type="bullet">
/// <item>A larger unit is used when the amount fits one of its common measures within tolerance (16 Tbsp → 1 cup,
/// 12 tsp → ¼ cup, 24 oz → 1½ lb), BR-24.</item>
/// <item>Otherwise the recipe's own unit is kept if the amount rounds within tolerance and is not tiny or huge in it.</item>
/// <item>Otherwise a smaller unit (0.6 cup → 9½ Tbsp when ±2% is needed); else the nearest fraction.</item>
/// <item>An amount converted from the other system takes the largest unit it rounds to well (500 g → 1⅛ lb).</item>
/// </list>
/// Tolerance is BR-21's: ±2% for baking/precision recipes, ±8% otherwise.
/// </summary>
public static class MeasureDisplay
{
    public const decimal PrecisionTolerance = 0.02m;
    public const decimal StandardTolerance = 0.08m;

    public static decimal Tolerance(bool isPrecision) => isPrecision ? PrecisionTolerance : StandardTolerance;

    private sealed record Rung(string Code, decimal MinAsTarget, decimal MinAsSource, decimal? Cap, IReadOnlyList<int> CommonMeasures);

    // Smallest first. CommonMeasures: the fractions a larger unit must hit to be chosen over the recipe's own unit.
    private static readonly Rung[] UsVolume =
    [
        new("tsp", 0m, 0m, 12m, Fractions.Teaspoon),
        new("tbsp", 1m, 0.5m, 16m, [2]),
        new("cup", 0.25m, 0.125m, null, [2, 3, 4]),
    ];

    private static readonly Rung[] UsMass =
    [
        new("oz", 0m, 0m, 32m, [2, 4]),
        new("lb", 1m, 0.25m, null, [2, 4]),
    ];

    private static readonly Rung[] MetricVolume =
    [
        new("ml", 0m, 0m, 1000m, []),
        new("l", 1m, 0.25m, null, []),
    ];

    private static readonly Rung[] MetricMass =
    [
        new("g", 0m, 0m, 1000m, []),
        new("kg", 1m, 0.25m, null, []),
    ];

    /// <summary>
    /// Shows <paramref name="baseAmount"/> (ml for volume, g for mass) in <paramref name="system"/>.
    /// <paramref name="sourceUnit"/> is the unit the recipe used, when it belongs to the same kind and system.
    /// </summary>
    public static DisplayAmount Show(decimal baseAmount, UnitKind kind, UnitSystem system, UnitDefinition? sourceUnit,
        decimal tolerance, MeasurementCatalog? catalog = null)
    {
        catalog ??= MeasurementCatalog.Default;
        var ladder = Ladder(kind, system);
        var source = sourceUnit is not null && ladder.Any(r => r.Code == sourceUnit.Code) ? sourceUnit.Code : null;

        if (system == UnitSystem.Metric)
        {
            return ShowMetric(baseAmount, ladder, source, catalog);
        }

        // fl oz, pints, quarts and gallons stay in the unit the recipe used.
        if (sourceUnit is not null && source is null && sourceUnit.Kind == kind && sourceUnit.System == system)
        {
            return Make(Fractions.Nearest(baseAmount / sourceUnit.ToBase, Fractions.Standard), sourceUnit);
        }

        var sourceIndex = source is null ? -1 : Array.FindIndex(ladder, r => r.Code == source);

        // 1. A larger unit than the recipe's, largest first, at a common measure (BR-24). When converting from
        //    another system there is no recipe unit, and step 3 picks the largest unit that rounds well.
        for (var i = ladder.Length - 1; sourceIndex >= 0 && i > sourceIndex; i--)
        {
            var rung = ladder[i];
            var unit = catalog.Unit(rung.Code)!;
            var value = baseAmount / unit.ToBase;
            var common = Fractions.Nearest(value, rung.CommonMeasures);
            if (value >= rung.MinAsTarget && common.Error <= tolerance)
            {
                return Make(common, unit);
            }
        }

        // 2. The recipe's own unit.
        if (sourceIndex >= 0)
        {
            var rung = ladder[sourceIndex];
            var unit = catalog.Unit(rung.Code)!;
            var value = baseAmount / unit.ToBase;
            var rounded = Fractions.Nearest(value, Denominators(rung.Code));
            if (value >= rung.MinAsSource && (rung.Cap is null || value <= rung.Cap) && rounded.Error <= tolerance)
            {
                return Make(rounded, unit);
            }
        }

        // 3. A smaller unit, largest first.
        for (var i = (sourceIndex >= 0 ? sourceIndex : ladder.Length) - 1; i >= 0; i--)
        {
            var rung = ladder[i];
            var unit = catalog.Unit(rung.Code)!;
            var value = baseAmount / unit.ToBase;
            var rounded = Fractions.Nearest(value, Denominators(rung.Code));
            if (value >= rung.MinAsTarget && (rung.Cap is null || value <= rung.Cap) && rounded.Error <= tolerance)
            {
                return Make(rounded, unit);
            }
        }

        // 4. Nothing fits the tolerance: the nearest fraction in the unit with the smallest error.
        DisplayAmount? best = null;
        for (var i = 0; i < ladder.Length; i++)
        {
            var rung = ladder[i];
            var unit = catalog.Unit(rung.Code)!;
            var value = baseAmount / unit.ToBase;
            var min = i == sourceIndex ? rung.MinAsSource : rung.MinAsTarget;
            if (value < min && i != 0)
            {
                continue;
            }

            var candidate = Make(Fractions.Nearest(value, Denominators(rung.Code)), unit);
            if (best is null || candidate.Error <= best.Error)
            {
                best = candidate;
            }
        }

        return best!;
    }

    /// <summary>The high end of a range, in the unit already chosen for the low end.</summary>
    public static DisplayAmount InUnit(decimal baseAmount, UnitDefinition unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var value = baseAmount / unit.ToBase;
        if (unit.System == UnitSystem.Metric)
        {
            var text = Fractions.FormatMetric(value, unit.Code);
            var shown = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            return new DisplayAmount(shown, unit, text, Fractions.RelativeError(value, shown));
        }

        return Make(Fractions.Nearest(value, Denominators(unit.Code)), unit);
    }

    /// <summary>The unit name for an amount: "cup" for 1 or less, "cups" above (UX-05).</summary>
    public static string UnitName(UnitDefinition unit, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return amount > 1m ? unit.PluralName : unit.Name;
    }

    private static DisplayAmount ShowMetric(decimal baseAmount, Rung[] ladder, string? source, MeasurementCatalog catalog)
    {
        var small = catalog.Unit(ladder[0].Code)!;
        var large = catalog.Unit(ladder[1].Code)!;
        var useLarge = source == large.Code ? baseAmount >= large.ToBase * ladder[1].MinAsSource : baseAmount >= large.ToBase;
        return InUnit(baseAmount, useLarge ? large : small);
    }

    private static Rung[] Ladder(UnitKind kind, UnitSystem system) => (kind, system) switch
    {
        (UnitKind.Mass, UnitSystem.Metric) => MetricMass,
        (UnitKind.Mass, _) => UsMass,
        (_, UnitSystem.Metric) => MetricVolume,
        _ => UsVolume,
    };

    private static readonly int[] TablespoonFractions = [2, 4];

    // BR-28 denominators; tablespoons use halves and quarters only (no "9⅝ Tbsp").
    private static IReadOnlyList<int> Denominators(string code) => code switch
    {
        "tsp" => Fractions.Teaspoon,
        "tbsp" => TablespoonFractions,
        _ => Fractions.Standard,
    };

    private static DisplayAmount Make(RoundedAmount rounded, UnitDefinition unit) =>
        new(rounded.Value, unit, Fractions.Format(rounded.Value), rounded.Error);
}
