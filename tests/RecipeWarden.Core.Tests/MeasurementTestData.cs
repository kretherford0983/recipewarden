using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>Shared lines and a small catalog with seed-like values (USDA SR Legacy, ADR 0013).</summary>
internal static class MeasurementTestData
{
    public const int Flour = 1;
    public const int ChickenBreast = 2;
    public const int Salt = 3;
    public const int Butter = 4;
    public const int ChickenBroth = 5;
    public const int Honey = 6;
    public const int DicedTomatoes = 7;

    public static MeasurementCatalog Catalog { get; } = new(
        foods:
        [
            new FoodFacts(Flour, "all-purpose flour", 0.5283m),
            new FoodFacts(ChickenBreast, "chicken breast", null),
            new FoodFacts(Salt, "salt", 1.2173m),
            new FoodFacts(Butter, "butter", 0.9595m),
            new FoodFacts(ChickenBroth, "chicken broth", 1.0525m, FoodForm.Liquid),
            new FoodFacts(Honey, "honey", 1.4329m, FoodForm.Liquid),
            new FoodFacts(DicedTomatoes, "diced tomatoes", 1.0144m),
        ],
        yields:
        [
            new YieldFacts(ChickenBreast, "cooked", "raw", 1.1364m, 1.4286m, 0.5917m, "boneless breast, simmered, about 70–88% cooking yield"),
        ]);

    public static ParsedIngredient Line(decimal? qty, string? unit, string name, string? prep = null, string? note = null,
        decimal? max = null, string? state = null, bool optional = false) => new()
    {
        RawText = $"{qty} {unit} {name}".Trim(),
        Quantity = qty,
        QuantityMax = max,
        UnitCode = unit,
        Name = name,
        Preparation = prep,
        Note = note,
        State = state,
        IsOptional = optional,
        Confidence = 1m,
    };

    public static ScaleContext Context(decimal factor, DisplayUnits units = DisplayUnits.AsWritten, bool precision = false) =>
        new(factor, units, precision, Catalog);

    public static string Scaled(ParsedIngredient line, decimal factor, DisplayUnits units = DisplayUnits.AsWritten,
        bool precision = false, int? food = null) =>
        IngredientScaler.Scale(new ScaleInput(line, food), Context(factor, units, precision)).Text;
}
