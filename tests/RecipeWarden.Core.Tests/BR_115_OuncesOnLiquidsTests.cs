using RecipeWarden.Core.Kitchen;
using RecipeWarden.Core.Measurement;
using static RecipeWarden.Core.Tests.MeasurementTestData;

namespace RecipeWarden.Core.Tests;

/// <summary>
/// BR-115, ADR 0049: a bare "oz" on a water-like liquid (Form = Liquid, density 0.85 to 1.15 g/ml) is fluid ounces
/// whenever it is converted, scaled or summed. Thick liquids, solids and unmatched lines keep weight; a line at 1x as
/// written is unchanged.
/// </summary>
[Trait("Req", "BR-115")]
public sealed class BR_115_OuncesOnLiquidsTests
{
    [Theory]
    [InlineData(0.85, true)]
    [InlineData(1.0525, true)]
    [InlineData(1.15, true)]
    [InlineData(0.84, false)]
    [InlineData(1.16, false)]
    public void Water_like_means_a_liquid_between_0_85_and_1_15_g_per_ml(double density, bool expected) =>
        OunceRules.IsWaterLikeLiquid(new FoodFacts(1, "liquid", (decimal)density, FoodForm.Liquid)).ShouldBe(expected);

    [Fact]
    public void Only_liquids_with_a_known_density_count()
    {
        OunceRules.IsWaterLikeLiquid(null).ShouldBeFalse();
        OunceRules.IsWaterLikeLiquid(new FoodFacts(1, "mystery liquid", null, FoodForm.Liquid)).ShouldBeFalse();
        OunceRules.IsWaterLikeLiquid(new FoodFacts(1, "diced tomatoes", 1.0144m)).ShouldBeFalse();
        OunceRules.IsWaterLikeLiquid(new FoodFacts(1, "butter", 0.9595m, FoodForm.Fat)).ShouldBeFalse();
    }

    [Fact]
    public void Only_a_bare_oz_changes()
    {
        var broth = Catalog.Food(ChickenBroth);
        OunceRules.Effective(UnitCatalog.FindByCode("oz")!, broth).Code.ShouldBe("floz");
        OunceRules.Effective(UnitCatalog.FindByCode("lb")!, broth).Code.ShouldBe("lb");
        OunceRules.Effective(UnitCatalog.FindByCode("floz")!, broth).Code.ShouldBe("floz");
        OunceRules.Effective(UnitCatalog.FindByCode("oz")!, Catalog.Food(Honey)).Code.ShouldBe("oz");
    }

    [Fact]
    public void At_1x_as_written_the_line_is_unchanged() =>
        Scaled(Line(24m, "oz", "chicken broth"), 1m, food: ChickenBroth).ShouldBe("24 oz chicken broth");

    [Fact]
    public void Broth_in_ounces_converts_as_a_volume()
    {
        var broth = Line(24m, "oz", "chicken broth");
        Scaled(broth, 1m, DisplayUnits.US, food: ChickenBroth).ShouldBe("24 fl oz chicken broth");
        Scaled(broth, 1m, DisplayUnits.Metric, food: ChickenBroth).ShouldBe("710 ml chicken broth");
    }

    [Fact]
    public void Scaled_broth_is_never_pounds_or_grams()
    {
        foreach (var units in new[] { DisplayUnits.AsWritten, DisplayUnits.US, DisplayUnits.Metric })
        {
            var text = Scaled(Line(24m, "oz", "chicken broth"), 2m, units, food: ChickenBroth);
            text.ShouldNotContain(" lb");
            text.ShouldNotContain(" g ");
            text.ShouldNotMatch(@"\d oz ", "a bare oz would read as weight");
        }

        Scaled(Line(24m, "oz", "chicken broth"), 2m, food: ChickenBroth).ShouldBe("48 fl oz chicken broth");
        Scaled(Line(24m, "oz", "chicken broth"), 2m, DisplayUnits.Metric, food: ChickenBroth).ShouldBe("1.42 l chicken broth");
    }

    [Fact]
    public void Weight_view_weighs_broth_by_its_density_and_keeps_the_volume_second()
    {
        var scaled = IngredientScaler.Scale(new ScaleInput(Line(24m, "oz", "chicken broth"), ChickenBroth), Context(1m, DisplayUnits.Weight));
        scaled.Text.ShouldBe("747 g chicken broth");
        scaled.Secondary.ShouldBe("24 fl oz");
    }

    [Fact]
    public void Thick_liquids_solids_and_unmatched_lines_keep_weight()
    {
        Scaled(Line(12m, "oz", "honey"), 2m, DisplayUnits.US, food: Honey).ShouldBe("1½ lb honey");
        Scaled(Line(12m, "oz", "diced tomatoes"), 2m, DisplayUnits.US, food: DicedTomatoes).ShouldBe("1½ lb diced tomatoes");
        Scaled(Line(12m, "oz", "mystery liquid"), 2m, DisplayUnits.US).ShouldBe("1½ lb mystery liquid");
    }

    [Fact]
    public void A_scale_in_my_kitchen_does_not_turn_a_written_ounce_line_into_grams_at_1x()
    {
        var tools = KitchenTestData.StandardWith(KitchenTestData.Scale());
        var context = new ScaleContext(1m, DisplayUnits.AsWritten, true, Catalog, tools, PreferWeight: true);
        var scaled = IngredientScaler.Scale(new ScaleInput(Line(24m, "oz", "chicken broth"), ChickenBroth), context);
        scaled.Text.ShouldBe("24 oz chicken broth");
        scaled.ByWeight.ShouldBeFalse();
    }

    [Fact]
    public void The_shown_volume_feeds_the_container_rounding_check()
    {
        var scaled = IngredientScaler.Scale(new ScaleInput(Line(24m, "oz", "chicken broth"), ChickenBroth), Context(1m));
        scaled.ExactMl!.Value.ShouldBe(24m * 29.5735295625m);
    }

    [Fact]
    [Trait("Req", "BR-111")]
    public void The_container_estimate_counts_broth_ounces_as_fluid_ounces()
    {
        var estimate = ContainerRules.Estimate(
        [
            new ScaleInput(Line(24m, "oz", "chicken broth"), ChickenBroth),
            new ScaleInput(Line(1m, "cup", "whole milk")),
        ], Catalog);

        estimate.Ml.ShouldBe(24m * 29.5735295625m + 236.5882365m);
        estimate.Excluded.ShouldBeEmpty();
    }
}
