using System.Globalization;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Nutrition;

/// <summary>The nine panel nutrients (FR-NUT-01). Per 100 g in a nutrient row, per serving on a recipe.</summary>
public sealed record NutrientValues(
    decimal? Calories = null,
    decimal? FatG = null,
    decimal? SatFatG = null,
    decimal? CholesterolMg = null,
    decimal? SodiumMg = null,
    decimal? CarbsG = null,
    decimal? FiberG = null,
    decimal? SugarG = null,
    decimal? ProteinG = null)
{
    public static NutrientValues Zero { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0);

    public bool IsEmpty => Calories is null && FatG is null && SatFatG is null && CholesterolMg is null && SodiumMg is null
                           && CarbsG is null && FiberG is null && SugarG is null && ProteinG is null;

    public NutrientValues Times(decimal factor) => Map(v => v * factor);

    /// <summary>Adds two sets; a value missing on one side counts as 0 (a food without a sugar value adds no sugar).</summary>
    public NutrientValues Plus(NutrientValues other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new(Sum(Calories, other.Calories), Sum(FatG, other.FatG), Sum(SatFatG, other.SatFatG), Sum(CholesterolMg, other.CholesterolMg),
            Sum(SodiumMg, other.SodiumMg), Sum(CarbsG, other.CarbsG), Sum(FiberG, other.FiberG), Sum(SugarG, other.SugarG),
            Sum(ProteinG, other.ProteinG));
    }

    public NutrientValues Map(Func<decimal, decimal> f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return new(Calories is { } a ? f(a) : null, FatG is { } b ? f(b) : null, SatFatG is { } c ? f(c) : null,
            CholesterolMg is { } d ? f(d) : null, SodiumMg is { } e ? f(e) : null, CarbsG is { } g ? f(g) : null,
            FiberG is { } h ? f(h) : null, SugarG is { } i ? f(i) : null, ProteinG is { } j ? f(j) : null);
    }

    private static decimal? Sum(decimal? a, decimal? b) => a is null && b is null ? null : (a ?? 0) + (b ?? 0);
}

/// <summary>Where a recipe's panel comes from (BR-50).</summary>
public enum NutritionSource
{
    Calculated,
    Imported,
    Manual,
}

/// <summary>
/// What a food brings to the calculation: its nutrients per 100 g, a density for volumes, portion weights for counts
/// ("large" → 50 g) and, when known, its package ("can" → 425 g).
/// </summary>
public sealed record NutritionFood(
    NutrientValues Per100G,
    decimal? DensityGPerMl = null,
    IReadOnlyDictionary<string, decimal>? Portions = null,
    string? PackageName = null,
    decimal? PackageGrams = null);

/// <summary>One recipe line for the calculation: the parsed line, its food (null when unmatched), purchase grams from a yield conversion.</summary>
public sealed record NutritionLineInput(ParsedIngredient Line, NutritionFood? Food, decimal? PurchaseGrams = null);

/// <summary>How a line counted.</summary>
public enum NutritionLineState
{
    /// <summary>Weighed and matched to nutrients.</summary>
    Matched,

    /// <summary>No food or no nutrients for it (it lowers completeness and is in the unmatched report).</summary>
    NoNutrients,

    /// <summary>A food, but no way to weigh this amount (no density, no portion).</summary>
    NoWeight,

    /// <summary>"To taste" or no amount: not counted.</summary>
    NotCounted,
}

public sealed record NutritionLine(string Text, NutritionLineState State, decimal? Grams, NutrientValues? Values);

/// <summary>A recipe's calculated nutrition (FR-NUT-02): per serving, with completeness.</summary>
public sealed record NutritionResult(NutrientValues PerServing, int Counted, int Matched, IReadOnlyList<NutritionLine> Lines)
{
    /// <summary>Share of counted lines matched, 0–100; 100 when nothing is counted.</summary>
    public int CompletenessPct => Counted == 0 ? 100 : (int)Math.Floor(Matched * 100m / Counted);

    /// <summary>BR-50: below 70% the panel says "partial".</summary>
    public bool IsPartial => CompletenessPct < NutritionRules.PartialBelowPct;

    public bool HasAny => Matched > 0;
}

/// <summary>The panel to show: its values, source and (for calculated values) completeness.</summary>
public sealed record NutritionPanel(NutrientValues PerServing, NutritionSource Source, int? CompletenessPct)
{
    public bool IsPartial => Source == NutritionSource.Calculated && CompletenessPct < NutritionRules.PartialBelowPct;
}

/// <summary>
/// FR-NUT-01, FR-NUT-02, FR-NUT-04, BR-50, BR-51, AC-55, AC-57, ADR 0035: weighs each line, adds up nutrients and gives
/// per-serving values with completeness, picks the panel by precedence, and turns a package label into per-100 g values.
/// </summary>
public static class NutritionRules
{
    public const int PartialBelowPct = 70;
    public const decimal StickMl = 118.29411825m;

    private static readonly string[] PreferredPortions = ["medium", "large", "whole", "small", "item", "piece", "each", "fruit", "serving"];

    private static readonly HashSet<string> VolumeWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "cup", "cups", "tbsp", "tablespoon", "tablespoons", "tsp", "teaspoon", "teaspoons", "fl", "oz", "ounce", "ounces", "lb",
        "pound", "pounds", "g", "gram", "grams", "ml", "liter", "litre", "quart", "pint", "gallon",
    };

    /// <summary>
    /// The weight of one line in grams, or null when it cannot be weighed. Purchase grams (a yield conversion) win; then
    /// weight units, volumes with a density, and counts with a portion or package weight.
    /// </summary>
    public static decimal? Grams(ParsedIngredient line, NutritionFood food, decimal? purchaseGrams = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(food);
        if (purchaseGrams is { } bought && bought > 0)
        {
            return bought;
        }

        if (line.Quantity is not { } low || low <= 0)
        {
            return null;
        }

        var quantity = line.QuantityMax is { } high && high > low ? (low + high) / 2 : low;
        var unit = UnitCatalog.FindByCode(line.UnitCode);
        if (unit is null)
        {
            return line.UnitCode is null ? quantity * PortionFor(food, line.RawText + " " + line.Name) : null;
        }

        switch (unit.Kind)
        {
            case UnitKind.Mass:
                return quantity * unit.ToBase;
            case UnitKind.Volume or UnitKind.Vague:
                return food.DensityGPerMl is { } d && d > 0 ? quantity * unit.ToBase * d : null;
            case UnitKind.Count:
                if (unit.Code == "stick" && food.DensityGPerMl is { } sd && sd > 0)
                {
                    return quantity * StickMl * sd;
                }

                if (food.PackageGrams is { } pg && pg > 0 && string.Equals(food.PackageName, unit.Code, StringComparison.OrdinalIgnoreCase))
                {
                    return quantity * pg;
                }

                return food.Portions is { } portions && portions.TryGetValue(unit.Code, out var each) && each > 0 ? quantity * each : null;
            default:
                return null;
        }
    }

    /// <summary>A count's weight: a portion named in the line ("large"), else the usual one ("medium", "large", …), else the first.</summary>
    private static decimal? PortionFor(NutritionFood food, string text)
    {
        if (food.Portions is not { Count: > 0 } portions)
        {
            return null;
        }

        foreach (var word in text.ToLowerInvariant().Split([' ', ',', '(', ')', '-'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (portions.TryGetValue(word, out var named) && named > 0)
            {
                return named;
            }
        }

        foreach (var key in PreferredPortions)
        {
            if (portions.TryGetValue(key, out var usual) && usual > 0)
            {
                return usual;
            }
        }

        return portions.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => (decimal?)p.Value).FirstOrDefault(v => v > 0);
    }

    /// <summary>
    /// FR-NUT-02, BR-51, ADR 0035: adds up the lines and divides by the servings (the low end of a range). Lines without
    /// an amount ("to taste") are not counted; vague amounts count as matched.
    /// </summary>
    public static NutritionResult Calculate(IEnumerable<NutritionLineInput> lines, decimal? servings)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var total = NutrientValues.Zero;
        var result = new List<NutritionLine>();
        int counted = 0, matched = 0;
        foreach (var input in lines)
        {
            var line = input.Line;
            if (line.Quantity is null && input.PurchaseGrams is null)
            {
                result.Add(new NutritionLine(line.RawText, NutritionLineState.NotCounted, null, null));
                continue;
            }

            counted++;
            if (input.Food is not { } food || food.Per100G.IsEmpty)
            {
                result.Add(new NutritionLine(line.RawText, NutritionLineState.NoNutrients, null, null));
                continue;
            }

            var grams = Grams(line, food, input.PurchaseGrams);
            var vague = UnitCatalog.FindByCode(line.UnitCode)?.IsVague == true;
            if (grams is null && !vague)
            {
                result.Add(new NutritionLine(line.RawText, NutritionLineState.NoWeight, null, null));
                continue;
            }

            matched++;
            var values = food.Per100G.Times((grams ?? 0) / 100m);
            total = total.Plus(values);
            result.Add(new NutritionLine(line.RawText, NutritionLineState.Matched, grams, values));
        }

        var per = servings is { } s && s > 0 ? total.Times(1 / s) : total;
        return new NutritionResult(per, counted, matched, result);
    }

    /// <summary>BR-50: manual, then imported, then calculated (only when something matched).</summary>
    public static NutritionPanel? Choose(NutrientValues? manual, NutrientValues? imported, NutritionResult? calculated)
    {
        if (manual is { IsEmpty: false })
        {
            return new NutritionPanel(manual, NutritionSource.Manual, null);
        }

        if (imported is { IsEmpty: false })
        {
            return new NutritionPanel(imported, NutritionSource.Imported, null);
        }

        return calculated is { HasAny: true } c ? new NutritionPanel(c.PerServing, NutritionSource.Calculated, c.CompletenessPct) : null;
    }

    public const string BadServing = "Give the label's serving size (above 0 and at most 5,000).";
    public const string BadValue = "Label values must be numbers of 0 or more.";
    public const string NoValues = "Enter at least the calories from the label.";

    /// <summary>
    /// FR-NUT-04, AC-57: label values per serving → per 100 g. A serving in ml uses <paramref name="densityGPerMl"/>
    /// (1 g/ml when not given).
    /// </summary>
    public static (NutrientValues? Per100G, string? Error) FromLabel(decimal servingSize, bool servingInMl, decimal? densityGPerMl, NutrientValues label)
    {
        ArgumentNullException.ThrowIfNull(label);
        if (servingSize <= 0 || servingSize > 5000 || densityGPerMl is <= 0 or > 5)
        {
            return (null, BadServing);
        }

        if (label.Calories is null)
        {
            return (null, NoValues);
        }

        var values = new[] { label.Calories, label.FatG, label.SatFatG, label.CholesterolMg, label.SodiumMg, label.CarbsG, label.FiberG, label.SugarG, label.ProteinG };
        if (values.Any(v => v is < 0 or > 100_000))
        {
            return (null, BadValue);
        }

        var grams = servingInMl ? servingSize * (densityGPerMl ?? 1m) : servingSize;
        return (label.Times(100m / grams).Map(v => Math.Round(v, 2)), null);
    }

    /// <summary>The key for a USDA portion ("large", "clove", "medium (2-1/2" dia)" → "medium"); null for volume and weight portions.</summary>
    public static string? PortionKey(string? modifier, string? unitName)
    {
        var text = string.IsNullOrWhiteSpace(unitName) || unitName is "undetermined" ? modifier : unitName;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var word = text.Trim().ToLowerInvariant().Split([' ', ',', '(', ')', '"'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (word is null || VolumeWords.Contains(word) || word.Any(char.IsDigit))
        {
            return null;
        }

        word = word.TrimEnd('.');
        return word switch
        {
            "cloves" => "clove",
            "slices" => "slice",
            "stalks" => "stalk",
            "sprigs" => "sprig",
            "heads" => "head",
            "pieces" => "piece",
            "extra" => "extra large",
            _ => word,
        };
    }

    /// <summary>g/ml from a USDA volume portion ("1 cup" = 125 g), or null when the portion is not a cup, tablespoon or teaspoon.</summary>
    public static decimal? DensityFromPortion(string? modifier, string? unitName, decimal amount, decimal grams)
    {
        if (amount <= 0 || grams <= 0)
        {
            return null;
        }

        var text = (string.IsNullOrWhiteSpace(unitName) || unitName is "undetermined" ? modifier : unitName)?.Trim().ToLowerInvariant();
        var first = text?.Split([' ', ',', '('], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var code = first switch
        {
            "cup" or "cups" => "cup",
            "tbsp" or "tablespoon" or "tablespoons" => "tbsp",
            "tsp" or "teaspoon" or "teaspoons" => "tsp",
            _ => null,
        };
        return code is null || UnitCatalog.FindByCode(code) is not { } unit ? null : Math.Round(grams / (amount * unit.ToBase), 4);
    }

    /// <summary>"410", "14.5", "1,200" for the panel.</summary>
    public static string Format(decimal? value, int decimals = 0) =>
        value is { } v ? Math.Round(v, decimals, MidpointRounding.AwayFromZero).ToString(decimals == 0 ? "#,0" : "#,0.#", CultureInfo.InvariantCulture) : "—";
}
