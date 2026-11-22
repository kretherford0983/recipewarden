using RecipeWarden.Core.Kitchen;

namespace RecipeWarden.Core.Measurement;

/// <summary>One tool used <see cref="Fills"/> times.</summary>
public sealed record ToolFill(KitchenToolInfo Tool, int Fills);

/// <summary>
/// A display plan (9.4): the tools to fill, the text ("1 cup + 2 Tbsp"), and the explanation — the exact amount in
/// ml, what the plan measures, and the error (UX-06 tooltip).
/// </summary>
public sealed record ToolPlan(IReadOnlyList<ToolFill> Fills, string Text, decimal ExactMl, decimal PlannedMl, decimal Error)
{
    public int FillCount => Fills.Sum(f => f.Fills);

    public string Explain() =>
        "Exact: " + Fractions.Format(Fractions.Nearest(ExactMl / UnitCatalog.FindByCode("tsp")!.ToBase, Fractions.Teaspoon).Value)
        + " tsp (" + Math.Round(ExactMl, 1).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " ml); this plan is "
        + Math.Round(Error * 100m, 1).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "% off.";
}

/// <summary>
/// FR-MEAS-02, BR-21, BR-24, ADR 0018: combines the active spoons and cups to make an amount. At most 3 fills;
/// the fewest fills within tolerance wins, then the smallest error. Returns null when nothing fits, and the caller
/// falls back to the nearest fraction (ADR 0012). Large amounts therefore stay "5 cups" (5 fills is over the cap).
/// </summary>
public static class DisplayPlanner
{
    public const int MaxFills = 3;

    public static ToolPlan? Plan(decimal ml, KitchenTools tools, decimal tolerance)
    {
        ArgumentNullException.ThrowIfNull(tools);
        if (ml <= 0m)
        {
            return null;
        }

        // Near-exact first: the fewest fills within ±2%, so 1⅛ cups is "1 cup + 2 Tbsp" (AC-32), not "1 cup + 1 Tbsp"
        // (5.6% off). Only when nothing is that close, the fewest fills within the recipe's tolerance (BR-21).
        foreach (var stage in new[] { Math.Min(MeasureDisplay.PrecisionTolerance, tolerance), tolerance }.Distinct())
        {
            if (Search(ml, tools.Measures, stage) is { } plan)
            {
                return plan;
            }
        }

        return null;
    }

    private static ToolPlan? Search(decimal ml, IReadOnlyList<KitchenToolInfo> measures, decimal tolerance)
    {
        ToolPlan? best = null;
        for (var fills = 1; fills <= MaxFills && best is null; fills++)
        {
            foreach (var combo in Combinations(measures.Count, fills))
            {
                var planned = combo.Sum(i => measures[i].CapacityMl!.Value);
                var error = Math.Abs(planned - ml) / ml;
                if (error <= tolerance && (best is null || error < best.Error))
                {
                    var grouped = combo.GroupBy(i => i).Select(g => new ToolFill(measures[g.Key], g.Count())).ToList();
                    best = new ToolPlan(grouped, Text(grouped), ml, planned, error);
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Largest unit first. Tools of one unit merge when their total is a BR-28 fraction ("1 cup + ¼ cup" → "1¼ cups",
    /// "2 × 1 Tbsp" → "2 Tbsp"); otherwise each tool is named ("⅓ cup + ¼ cup + 1 tsp").
    /// </summary>
    private static string Text(IReadOnlyList<ToolFill> fills)
    {
        var parts = new List<string>();
        foreach (var code in new[] { "cup", "tbsp", "tsp" })
        {
            var ofUnit = fills.Where(f => f.Tool.CapacityUnit == code).OrderByDescending(f => f.Tool.CapacityAmount).ToList();
            if (ofUnit.Count == 0)
            {
                continue;
            }

            var unit = UnitCatalog.FindByCode(code)!;
            var denominators = code == "tsp" ? Fractions.Teaspoon : Fractions.Standard;
            var total = ofUnit.Sum(f => f.Tool.CapacityAmount!.Value * f.Fills);
            var merged = Fractions.Nearest(total, denominators);
            if (merged.Error < 0.001m)
            {
                parts.Add(Fractions.Format(merged.Value) + " " + MeasureDisplay.UnitName(unit, merged.Value));
                continue;
            }

            foreach (var f in ofUnit)
            {
                var amount = Fractions.Nearest(f.Tool.CapacityAmount!.Value * f.Fills, denominators).Value;
                parts.Add(Fractions.Format(amount) + " " + MeasureDisplay.UnitName(unit, amount));
            }
        }

        return string.Join(" + ", parts);
    }

    /// <summary>Multisets of size <paramref name="k"/> from n indices (i ≤ j ≤ …), so each combination is tried once.</summary>
    private static IEnumerable<int[]> Combinations(int n, int k)
    {
        var idx = new int[k];
        while (true)
        {
            yield return (int[])idx.Clone();
            var p = k - 1;
            while (p >= 0 && idx[p] == n - 1)
            {
                p--;
            }

            if (p < 0)
            {
                yield break;
            }

            idx[p]++;
            for (var q = p + 1; q < k; q++)
            {
                idx[q] = idx[p];
            }
        }
    }
}
