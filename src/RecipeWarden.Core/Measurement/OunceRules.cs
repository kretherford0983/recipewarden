namespace RecipeWarden.Core.Measurement;

/// <summary>
/// BR-115, ADR 0049: a bare "oz" on a water-like liquid (Form = Liquid, density 0.85 to 1.15 g/ml: broth, milk, wine,
/// cream) is read as fluid ounces whenever the amount is converted, scaled or summed. Thick liquids (honey, syrups),
/// solids and unmatched lines keep weight. Callers still show a line at 1x as written unchanged.
/// </summary>
public static class OunceRules
{
    public const decimal MinDensity = 0.85m;
    public const decimal MaxDensity = 1.15m;

    /// <summary>True for a liquid whose density is close enough to water that its ounces are fluid ounces.</summary>
    public static bool IsWaterLikeLiquid(FoodFacts? food) =>
        food is { Form: FoodForm.Liquid, DensityGPerMl: { } d } && d >= MinDensity && d <= MaxDensity;

    /// <summary>The unit to compute with: fluid ounces for a bare "oz" on a water-like liquid, otherwise <paramref name="unit"/>.</summary>
    public static UnitDefinition Effective(UnitDefinition unit, FoodFacts? food)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return unit.Code == "oz" && IsWaterLikeLiquid(food) ? UnitCatalog.FindByCode("floz")! : unit;
    }
}
