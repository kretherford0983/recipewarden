using RecipeWarden.Core.Measurement;
using static RecipeWarden.Core.Tests.MeasurementTestData;

namespace RecipeWarden.Core.Tests;

/// <summary>
/// FR-MEAS-06, BR-27, AC-36: count items round to whole items; eggs to the nearest half, written "+ 1 yolk" for
/// ordinary recipes and "(… + 2 Tbsp beaten egg)" for baking/precision recipes (ADR 0012); cans as "+ about ⅓ more".
/// </summary>
[Trait("Req", "FR-MEAS-06")]
public sealed class FR_MEAS_06_CountItemTests
{
    [Fact]
    [Trait("Req", "AC-36")]
    public void Three_eggs_at_1_5x_in_an_ordinary_recipe()
    {
        Scaled(Line(3m, null, "eggs"), 1.5m).ShouldBe("4 eggs + 1 yolk");
    }

    [Fact]
    [Trait("Req", "AC-36")]
    public void Three_eggs_at_1_5x_in_a_baking_recipe()
    {
        Scaled(Line(3m, null, "eggs"), 1.5m, precision: true).ShouldBe("4½ eggs (4 eggs + 2 Tbsp beaten egg)");
    }

    [Theory]
    [Trait("Req", "AC-36")]
    [InlineData(1.5, false)]
    [InlineData(1.5, true)]
    [InlineData(1.7, false)]
    [InlineData(0.5, true)]
    [InlineData(2.5, false)]
    public void Eggs_never_show_as_decimals(double factor, bool precision)
    {
        var text = Scaled(Line(3m, null, "large eggs"), (decimal)factor, precision: precision);
        text.ShouldNotContain(".");
    }

    [Theory]
    [InlineData(2, 1.5, false, "3 eggs")]
    [InlineData(3, 1.2, false, "3 eggs + 1 yolk")]
    [InlineData(2, 0.5, false, "1 egg")]
    [InlineData(1, 0.5, false, "1 egg yolk")]
    [InlineData(1, 0.5, true, "½ egg (2 Tbsp beaten egg)")]
    [InlineData(1, 1.5, true, "1½ eggs (1 egg + 2 Tbsp beaten egg)")]
    [InlineData(1, 1.5, false, "1 egg + 1 yolk")]
    [InlineData(3, 2, false, "6 eggs")]
    public void Egg_rounding(int eggs, double factor, bool precision, string expected)
    {
        Scaled(Line(eggs, null, eggs == 1 ? "egg" : "eggs"), (decimal)factor, precision: precision).ShouldBe(expected);
    }

    [Fact]
    public void Egg_size_words_are_kept()
    {
        Scaled(Line(2m, null, "large eggs", prep: "beaten"), 2m).ShouldBe("4 large eggs, beaten");
    }

    [Theory]
    [InlineData("egg yolks", true)]
    [InlineData("egg whites", false)]
    [InlineData("egg noodles", false)]
    [InlineData("eggs", true)]
    [InlineData("Large Eggs", true)]
    [InlineData("eggplant", false)]
    public void Only_whole_eggs_use_the_egg_rule(string name, bool isEggOrYolk)
    {
        (EggRules.IsEgg(name) || name == "egg yolks").ShouldBe(isEggOrYolk);
    }

    [Fact]
    public void Yolks_and_whites_round_to_whole_items()
    {
        Scaled(Line(2m, null, "egg yolks"), 1.25m).ShouldBe("3 egg yolks");
    }

    [Theory]
    [InlineData(1.33, "1 can (15 oz) + about ⅓ more black beans, drained and rinsed")]
    [InlineData(2, "2 cans (15 oz) black beans, drained and rinsed")]
    [InlineData(0.5, "about ½ can (15 oz) black beans, drained and rinsed")]
    [InlineData(1.95, "2 cans (15 oz) black beans, drained and rinsed")]
    [InlineData(1.5, "1 can (15 oz) + about ½ more black beans, drained and rinsed")]
    public void Cans_show_whole_cans_plus_about_a_fraction_more(double factor, string expected)
    {
        Scaled(Line(1m, "can", "black beans", prep: "drained and rinsed", note: "15 oz"), (decimal)factor).ShouldBe(expected);
    }

    [Theory]
    [InlineData(1, null, "onion", 1.5, "2 onions")]
    [InlineData(1, null, "onion", 0.5, "½ onion")]
    [InlineData(2, null, "carrots", 0.5, "1 carrot")]
    [InlineData(3, "clove", "garlic", 1.5, "5 cloves garlic")]
    [InlineData(1, "clove", "garlic", 0.25, "¼ clove garlic")]
    [InlineData(1, "stick", "butter", 1.5, "1½ sticks butter")]
    [InlineData(2, null, "tomatoes", 3, "6 tomatoes")]
    [InlineData(1, null, "potato", 2, "2 potatoes")]
    [InlineData(1, null, "cherry", 2, "2 cherries")]
    public void Other_counts_round_to_whole_items(int qty, string? unit, string name, double factor, string expected)
    {
        Scaled(Line(qty, unit, name), (decimal)factor).ShouldBe(expected);
    }

    [Fact]
    public void Count_ranges_scale_both_ends()
    {
        Scaled(Line(2m, null, "eggs", max: 3m), 1.5m).ShouldBe("3–5 eggs");
    }
}
