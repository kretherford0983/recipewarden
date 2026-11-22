using RecipeWarden.Core.Kitchen;
using RecipeWarden.Core.Measurement;
using static RecipeWarden.Core.Tests.KitchenTestData;
using static RecipeWarden.Core.Tests.MeasurementTestData;

namespace RecipeWarden.Core.Tests;

/// <summary>BR-22, BR-23, AC-33, ADR 0018: weight first with a scale, a density, and a baking recipe or a dry food.</summary>
[Trait("Req", "BR-22")]
public sealed class BR_22_WeightFirstTests
{
    private static readonly KitchenTools WithScale = StandardWith(Scale());

    [Fact]
    [Trait("Req", "AC-33")]
    public void Baking_recipe_shows_flour_as_250_g_with_2_cups_second()
    {
        var scaled = KitchenTestData.Scale(Line(2m, "cup", "all-purpose flour"), KitchenTestData.Context(1m, WithScale, precision: true, preferWeight: true), FlourId);
        scaled.Text.ShouldBe("250 g all-purpose flour");
        scaled.Secondary.ShouldBe("2 cups");
        scaled.ByWeight.ShouldBeTrue();
    }

    [Fact]
    [Trait("Req", "AC-33")]
    [Trait("Req", "BR-23")]
    public void Without_a_density_the_original_volume_is_kept()
    {
        var scaled = KitchenTestData.Scale(Line(2m, "cup", "mystery flour"), KitchenTestData.Context(1m, WithScale, precision: true, preferWeight: true));
        scaled.Text.ShouldBe("2 cups mystery flour");
        scaled.ByWeight.ShouldBeFalse();
    }

    [Fact]
    public void Ordinary_recipes_weigh_dry_foods_but_not_liquids()
    {
        var ctx = KitchenTestData.Context(1m, WithScale, preferWeight: true);
        KitchenTestData.Scale(Line(1m, "cup", "granulated sugar"), ctx, SugarId).Text.ShouldBe("200 g granulated sugar");
        KitchenTestData.Scale(Line(1m, "cup", "whole milk"), ctx, MilkId).Text.ShouldBe("1 cup whole milk");
    }

    [Fact]
    public void Baking_recipes_weigh_liquids_too()
    {
        KitchenTestData.Scale(Line(1m, "cup", "whole milk"), KitchenTestData.Context(1m, WithScale, precision: true, preferWeight: true), MilkId)
            .Text.ShouldBe("244 g whole milk");
    }

    [Fact]
    public void An_ounce_only_scale_shows_ounces_at_its_resolution()
    {
        var ozOnly = StandardWith(Scale(unit: "oz", resolution: 0.05m, shows: "oz"));
        KitchenTestData.Scale(Line(2m, "cup", "all-purpose flour"), KitchenTestData.Context(1m, ozOnly, precision: true, preferWeight: true), FlourId)
            .Text.ShouldBe("8.8 oz all-purpose flour");
    }

    [Theory]
    [InlineData(5, "250 g")]
    [InlineData(0.1, "250 g")]
    [InlineData(2, "250 g")]
    public void Weight_rounds_to_the_scale_resolution(double resolution, string expected)
    {
        var tools = StandardWith(Scale(resolution: (decimal)resolution));
        KitchenTestData.Scale(Line(2m, "cup", "all-purpose flour"), KitchenTestData.Context(1m, tools, precision: true, preferWeight: true), FlourId)
            .Text.ShouldStartWith(expected);
        KitchenTestData.Scale(Line(1m, "tbsp", "all-purpose flour"), KitchenTestData.Context(1m, StandardWith(Scale(resolution: 5m)), precision: true, preferWeight: true), FlourId)
            .Text.ShouldBe("10 g all-purpose flour"); // 7.8 g on a 5 g scale
    }

    [Fact]
    public void No_scale_or_not_accepted_means_volume()
    {
        KitchenTestData.Scale(Line(2m, "cup", "all-purpose flour"), KitchenTestData.Context(1m, KitchenTools.Standard, precision: true, preferWeight: true), FlourId)
            .Text.ShouldBe("2 cups all-purpose flour");
        KitchenTestData.Scale(Line(2m, "cup", "all-purpose flour"), KitchenTestData.Context(1m, WithScale, precision: true, preferWeight: false), FlourId)
            .Text.ShouldBe("2 cups all-purpose flour");
    }

    [Fact]
    public void Scaled_weights_follow_the_factor_and_metric_view_keeps_m2_rules()
    {
        var ctx = KitchenTestData.Context(1.5m, WithScale, precision: true, preferWeight: true);
        KitchenTestData.Scale(Line(2m, "cup", "all-purpose flour"), ctx, Flour).Text.ShouldBe("375 g all-purpose flour");
        KitchenTestData.Scale(Line(2m, "cup", "all-purpose flour"), ctx with { Units = DisplayUnits.Metric }, Flour).Text.ShouldBe("710 ml all-purpose flour");
    }
}
