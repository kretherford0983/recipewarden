using RecipeWarden.Core.Kitchen;
using RecipeWarden.Core.Measurement;

namespace RecipeWarden.Core.Tests;

/// <summary>Tools and foods with forms for the M3 tests (ADR 0018).</summary>
internal static class KitchenTestData
{
    public const int FlourId = 1;
    public const int MilkId = 2;
    public const int SugarId = 3;

    public static KitchenToolInfo Scale(int id = 50, string unit = "g", decimal resolution = 1m, params string[] shows) =>
        new(id, "Digital scale", ToolType.Scale, Resolution: resolution, ResolutionUnit: unit, SupportedUnits: shows.Length == 0 ? ["g", "oz"] : shows);

    public static KitchenTools StandardWith(params KitchenToolInfo[] extra) =>
        KitchenTools.From(KitchenRules.StandardSet.Select((t, i) => t with { Id = i + 1 }).Concat(extra));

    public static MeasurementCatalog Catalog { get; } = new(foods:
    [
        new FoodFacts(FlourId, "all-purpose flour", 0.5283m, FoodForm.Dry),
        new FoodFacts(MilkId, "whole milk", 1.0313m, FoodForm.Liquid),
        new FoodFacts(SugarId, "granulated sugar", 0.8454m, FoodForm.Dry),
    ]);

    public static ScaleContext Context(decimal factor, KitchenTools? tools, bool precision = false, bool preferWeight = false,
        DisplayUnits units = DisplayUnits.AsWritten) =>
        new(factor, units, precision, Catalog, tools, preferWeight);

    public static ScaledIngredient Scale(Recipes.ParsedIngredient line, ScaleContext context, int? food = null) =>
        IngredientScaler.Scale(new ScaleInput(line, food), context);
}
