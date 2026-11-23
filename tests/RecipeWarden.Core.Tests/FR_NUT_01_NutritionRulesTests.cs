using RecipeWarden.Core.Nutrition;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-NUT-01, FR-NUT-02, FR-NUT-04, BR-50, BR-51, AC-55, AC-57, ADR 0035: the nutrition calculation.</summary>
[Trait("Req", "FR-NUT-02")]
public sealed class FR_NUT_01_NutritionRulesTests
{
    private static readonly NutritionFood Flour = new(new NutrientValues(364, 1, 0.2m, 0, 2, 76, 2.7m, 0.3m, 10), 0.5283m,
        new Dictionary<string, decimal>());

    private static readonly NutritionFood Egg = new(new NutrientValues(143, 9.5m, 3.1m, 372, 142, 0.7m, 0, 0.4m, 12.6m), null,
        new Dictionary<string, decimal> { ["large"] = 50, ["medium"] = 44, ["small"] = 38 });

    private static readonly NutritionFood Garlic = new(new NutrientValues(149, ProteinG: 6.4m), 0.575m,
        new Dictionary<string, decimal> { ["clove"] = 3 });

    private static readonly NutritionFood Beans = new(new NutrientValues(91, ProteinG: 6), null, null, "can", 425m);

    private static readonly NutritionFood Butter = new(new NutrientValues(717, 81), 0.959m);

    private static ParsedIngredient L(decimal? q, string? unit, string name, decimal? max = null) =>
        new() { RawText = $"{q} {unit} {name}".Trim(), Quantity = q, QuantityMax = max, UnitCode = unit, Name = name };

    [Theory]
    [InlineData(2, "cup", 250)]          // 2 × 236.6 ml × 0.5283
    [InlineData(100, "g", 100)]
    [InlineData(1, "lb", 454)]
    [InlineData(1, "tsp", 3)]
    public void Weight_and_volume_lines_are_weighed(decimal q, string unit, decimal grams) =>
        Math.Round(NutritionRules.Grams(L(q, unit, "flour"), Flour)!.Value).ShouldBe(grams);

    [Fact]
    public void Counts_use_named_usual_or_package_portions()
    {
        NutritionRules.Grams(L(2, null, "large eggs"), Egg).ShouldBe(100);
        NutritionRules.Grams(L(2, null, "eggs"), Egg).ShouldBe(88); // "medium" is the usual portion
        NutritionRules.Grams(L(3, "clove", "garlic"), Garlic).ShouldBe(9);
        NutritionRules.Grams(L(1, "can", "black beans"), Beans).ShouldBe(425);
        Math.Round(NutritionRules.Grams(L(1, "stick", "butter"), Butter)!.Value).ShouldBe(113);
        NutritionRules.Grams(L(2, null, "x"), new NutritionFood(NutrientValues.Zero, null, new Dictionary<string, decimal> { ["wedge"] = 10 })).ShouldBe(20);
        NutritionRules.Grams(L(1, "slice", "garlic"), Garlic).ShouldBeNull();
        NutritionRules.Grams(L(2, null, "beans"), Beans).ShouldBeNull();
        NutritionRules.Grams(L(1, "cup", "egg"), Egg).ShouldBeNull(); // no density
        NutritionRules.Grams(L(1, "zzz", "egg"), Egg).ShouldBeNull();
        NutritionRules.Grams(L(null, "cup", "flour"), Flour).ShouldBeNull();
        NutritionRules.Grams(L(2, null, "eggs", 4), Egg).ShouldBe(132); // a range uses its middle
        NutritionRules.Grams(L(4, "cup", "cooked chicken"), Flour, purchaseGrams: 700).ShouldBe(700); // BR-26
        NutritionRules.Grams(L(1, null, "x"), new NutritionFood(NutrientValues.Zero)).ShouldBeNull();
    }

    [Fact]
    [Trait("Req", "BR-51")]
    public void Per_serving_uses_the_low_end_and_completeness_counts_matched_lines()
    {
        var result = NutritionRules.Calculate([
            new(L(2, "cup", "flour"), Flour),
            new(L(2, null, "large eggs"), Egg),
            new(L(1, "pinch", "salt"), new NutritionFood(new NutrientValues(0, SodiumMg: 38758))), // vague, no density: matched, 0 g
            new(L(null, null, "pepper to taste"), null),
            new(L(1, "cup", "gochujang"), null),
        ], servings: 4);

        result.Counted.ShouldBe(4);
        result.Matched.ShouldBe(3);
        result.CompletenessPct.ShouldBe(75);
        result.IsPartial.ShouldBeFalse();
        Math.Round(result.PerServing.Calories!.Value).ShouldBe(Math.Round((250m * 3.64m + 100 * 1.43m) / 4));
        result.Lines.Select(l => l.State).ShouldBe([NutritionLineState.Matched, NutritionLineState.Matched, NutritionLineState.Matched,
            NutritionLineState.NotCounted, NutritionLineState.NoNutrients]);

        var noWeight = NutritionRules.Calculate([new(L(1, "cup", "egg"), Egg), new(L(1, "cup", "x"), new NutritionFood(new NutrientValues()))], null);
        noWeight.Lines.Select(l => l.State).ShouldBe([NutritionLineState.NoWeight, NutritionLineState.NoNutrients]);
        noWeight.CompletenessPct.ShouldBe(0);
        noWeight.IsPartial.ShouldBeTrue();
        noWeight.HasAny.ShouldBeFalse();
        NutritionRules.Calculate([], 4).CompletenessPct.ShouldBe(100);
    }

    [Fact]
    [Trait("Req", "AC-55")]
    [Trait("Req", "BR-50")]
    public void Manual_beats_imported_beats_calculated_and_calculated_below_70_is_partial()
    {
        var calc = NutritionRules.Calculate([new(L(1, "cup", "flour"), Flour), new(L(1, "cup", "x"), null), new(L(1, "cup", "y"), null)], 2);
        var imported = new NutrientValues(410, 14);
        NutritionRules.Choose(new NutrientValues(300), imported, calc)!.Source.ShouldBe(NutritionSource.Manual);
        NutritionRules.Choose(null, imported, calc)!.Source.ShouldBe(NutritionSource.Imported);
        NutritionRules.Choose(new NutrientValues(), imported, calc)!.PerServing.ShouldBe(imported);
        var panel = NutritionRules.Choose(null, null, calc)!;
        panel.Source.ShouldBe(NutritionSource.Calculated);
        panel.CompletenessPct.ShouldBe(33);
        panel.IsPartial.ShouldBeTrue();
        NutritionRules.Choose(null, null, NutritionRules.Calculate([new(L(1, "cup", "x"), null)], 1)).ShouldBeNull();
        new NutritionPanel(imported, NutritionSource.Imported, null).IsPartial.ShouldBeFalse();
    }

    [Fact]
    [Trait("Req", "AC-57")]
    [Trait("Req", "FR-NUT-04")]
    public void A_label_with_a_30_g_serving_and_110_kcal_is_about_367_kcal_per_100_g()
    {
        var (per100, error) = NutritionRules.FromLabel(30, false, null, new NutrientValues(110, 2, SodiumMg: 480));
        error.ShouldBeNull();
        per100!.Calories.ShouldBe(366.67m);
        per100.SodiumMg.ShouldBe(1600);
        per100.SugarG.ShouldBeNull();

        NutritionRules.FromLabel(15, true, 1.2m, new NutrientValues(60)).Per100G!.Calories.ShouldBe(333.33m); // 15 ml × 1.2 g/ml
        NutritionRules.FromLabel(100, true, null, new NutrientValues(50)).Per100G!.Calories.ShouldBe(50);  // 1 g/ml assumed
        NutritionRules.FromLabel(0, false, null, new NutrientValues(1)).Error.ShouldBe(NutritionRules.BadServing);
        NutritionRules.FromLabel(10, true, 0m, new NutrientValues(1)).Error.ShouldBe(NutritionRules.BadServing);
        NutritionRules.FromLabel(10, false, null, new NutrientValues(FatG: 1)).Error.ShouldBe(NutritionRules.NoValues);
        NutritionRules.FromLabel(10, false, null, new NutrientValues(10, -1)).Error.ShouldBe(NutritionRules.BadValue);
    }

    [Theory]
    [InlineData("large", null, "large")]
    [InlineData("extra large", null, "extra large")]
    [InlineData("medium (2-1/2\" dia)", null, "medium")]
    [InlineData("cloves", null, "clove")]
    [InlineData("slices", "undetermined", "slice")]
    [InlineData("cup, chopped", null, null)]
    [InlineData("tbsp", null, null)]
    [InlineData("oz", null, null)]
    [InlineData("1 serving", null, null)]
    [InlineData("", null, null)]
    [InlineData("x", "stalk", "stalk")]
    public void Portion_keys(string modifier, string? unit, string? key) => NutritionRules.PortionKey(modifier, unit).ShouldBe(key);

    [Theory]
    [InlineData("cup", 1, 125, 0.5283)]
    [InlineData("cup, sifted", 1, 125, 0.5283)]
    [InlineData("tbsp", 1, 14.2, 0.9603)]
    [InlineData("tsp", 2, 9.4, 0.9536)]
    public void Densities_come_from_volume_portions(string modifier, decimal amount, decimal grams, decimal density) =>
        NutritionRules.DensityFromPortion(modifier, "undetermined", amount, grams).ShouldBe(density);

    [Fact]
    public void Other_portions_give_no_density_and_values_format()
    {
        NutritionRules.DensityFromPortion("large", null, 1, 50).ShouldBeNull();
        NutritionRules.DensityFromPortion("cup", null, 0, 50).ShouldBeNull();
        NutritionRules.DensityFromPortion(null, null, 1, 50).ShouldBeNull();
        NutritionRules.Format(1234.5m).ShouldBe("1,235");
        NutritionRules.Format(2.04m, 1).ShouldBe("2");
        NutritionRules.Format(2.25m, 1).ShouldBe("2.3");
        NutritionRules.Format(null).ShouldBe("—");
        NutrientValues.Zero.IsEmpty.ShouldBeFalse();
        new NutrientValues().IsEmpty.ShouldBeTrue();
        new NutrientValues(1).Plus(new NutrientValues(FatG: 2)).ShouldBe(new NutrientValues(1, 2));
    }
}
