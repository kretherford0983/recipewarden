using RecipeWarden.Core.Nutrition;

namespace RecipeWarden.Core.Tests;

/// <summary>ADR 0052: FNDDS portions carry their amount in the words ("1/2 cup"), which the density rule needs apart.</summary>
[Trait("Req", "FR-NUT-05")]
public sealed class ADR_0052_PortionTextTests
{
    [Theory]
    [InlineData("1 cup", 1, "cup")]
    [InlineData("1/2 cup, diced", 0.5, "cup, diced")]
    [InlineData("1.5 oz", 1.5, "oz")]
    [InlineData("  2   tablespoons ", 2, "tablespoons")]
    [InlineData("Quantity not specified", 1, "Quantity not specified")]
    [InlineData("cup", 1, "cup")]
    [InlineData("0 cup", 1, "0 cup")]
    [InlineData("1/0 cup", 1, "1/0 cup")]
    [InlineData("x/2 cup", 1, "x/2 cup")]
    public void A_leading_amount_is_split_from_the_words(string text, double amount, string rest)
    {
        var (a, t) = NutritionRules.SplitPortion(text);
        a.ShouldBe((decimal)amount);
        t.ShouldBe(rest);
    }

    [Theory]
    [InlineData("sr_legacy_food", "SR Legacy")]
    [InlineData("foundation_food", "Foundation")]
    [InlineData("survey_fndds_food", "FNDDS")]
    [InlineData("branded_food", null)]
    [InlineData(null, null)]
    public void Search_results_name_their_dataset(string? type, string? label) => NutritionRules.DatasetLabel(type).ShouldBe(label);

    [Fact]
    public void A_split_fndds_cup_gives_a_density()
    {
        var (amount, words) = NutritionRules.SplitPortion("1/2 cup");
        NutritionRules.DensityFromPortion(words, "undetermined", amount, 122m).ShouldBe(Math.Round(122m / (0.5m * 236.5882365m), 4));
    }
}
