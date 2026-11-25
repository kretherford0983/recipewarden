using RecipeWarden.Core.Kitchen;
using RecipeWarden.Core.Measurement;

namespace RecipeWarden.Core.Import;

/// <summary>An estimated Makes for an import (BR-135): the text put in the field and the ingredients not counted.</summary>
public sealed record YieldPrefill(string Text, IReadOnlyList<string> Excluded);

/// <summary>
/// BR-135 (owner decision Q2, 2026-11-25; FR-SCL-06, BR-111): when an import does not say what it makes, Makes is
/// prefilled with the ingredient estimate, as US cups ("about 1 1/3 cups"; Tbsp under ¼ cup, quarts from 8 cups).
/// No estimate when nothing could be counted or when more than half the lines with an amount could not be.
/// The saved yield is an estimate (never entered) unless the Editor changes it.
/// </summary>
public static class ImportYieldRules
{
    private static readonly IReadOnlyList<int> Denominators = [2, 3, 4];

    public static YieldPrefill? Prefill(IReadOnlyList<ScaleInput> lines, MeasurementCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(catalog);
        var withAmounts = lines.Count(l => l.Ingredient.Quantity is not null || !string.IsNullOrEmpty(l.Ingredient.UnitCode));
        if (withAmounts == 0)
        {
            return null;
        }

        var estimate = ContainerRules.Estimate(lines, catalog);
        if (estimate.Ml <= 0m || estimate.Excluded.Count * 2 > withAmounts)
        {
            return null;
        }

        return new YieldPrefill(Format(estimate.Ml), estimate.Excluded);
    }

    /// <summary>The review warning for a prefilled estimate (FR-IMP-06).</summary>
    public static string Warning(YieldPrefill prefill)
    {
        ArgumentNullException.ThrowIfNull(prefill);
        var excluded = prefill.Excluded.Count > 0 ? " Not counted: " + string.Join(", ", prefill.Excluded) + "." : string.Empty;
        return $"Makes was estimated from the ingredients: {prefill.Text}.{excluded} Check it against what the recipe really makes, or clear it.";
    }

    private static string Format(decimal ml)
    {
        var cup = UnitCatalog.FindByCode("cup")!;
        var unit = ml < cup.ToBase / 4m ? UnitCatalog.FindByCode("tbsp")! : ml >= cup.ToBase * 8m ? UnitCatalog.FindByCode("quart")! : cup;
        var value = Fractions.Nearest(ml / unit.ToBase, Denominators).Value;
        if (value <= 0m)
        {
            value = 1m / Denominators[^1];
        }

        return "about " + Plain(value) + " " + MeasureDisplay.UnitName(unit, value);
    }

    /// <summary>"1 1/3", "3/4", "3": typed-style fractions, as the editor's Makes field shows them.</summary>
    private static string Plain(decimal value)
    {
        var whole = decimal.Truncate(value);
        var fraction = value - whole;
        foreach (var d in Denominators)
        {
            var n = fraction * d;
            if (fraction > 0m && Math.Abs(n - Math.Round(n)) < 0.0001m)
            {
                var part = ((int)Math.Round(n)).ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" + d.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return whole == 0m ? part : whole.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + part;
            }
        }

        return whole.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
