using RecipeWarden.Core.Nutrition;
using RecipeWarden.Core.Python;

namespace RecipeWarden.Core.Tests;

/// <summary>BR-116, FR-NUT-07: only confident candidates that resolve to a local, current food are shown.</summary>
[Trait("Req", "FR-NUT-07")]
[Trait("Req", "BR-116")]
public sealed class FR_NUT_07_FoodSuggestionRulesTests
{
    private static FdcCandidate C(int id, decimal confidence) => new(id, "sr_legacy_food", "food " + id, confidence);

    [Fact]
    [Trait("Req", "AC-101")]
    public void The_most_confident_local_candidate_is_picked()
    {
        var picked = FoodSuggestionRules.Pick([C(1, 0.8m), C(2, 0.95m), C(3, 0.9m)], _ => true);
        picked!.FdcId.ShouldBe(2);
    }

    [Fact]
    [Trait("Req", "AC-102")]
    public void A_candidate_that_is_not_in_the_local_data_is_never_shown()
    {
        FoodSuggestionRules.Pick([C(9, 1m), C(2, 0.8m)], id => id == 2)!.FdcId.ShouldBe(2);
        FoodSuggestionRules.Pick([C(9, 1m)], _ => false).ShouldBeNull();
    }

    [Theory]
    [InlineData(0.74, false)]
    [InlineData(0.75, true)]
    [InlineData(1.0, true)]
    [InlineData(1.01, false)]
    [InlineData(-0.5, false)]
    public void Confidence_must_be_at_least_0_75_and_valid(double confidence, bool shown) =>
        (FoodSuggestionRules.Pick([C(1, (decimal)confidence)], _ => true) is not null).ShouldBe(shown);

    [Fact]
    public void Ties_keep_the_matchers_order() =>
        FoodSuggestionRules.Pick([C(5, 0.9m), C(6, 0.9m)], _ => true)!.FdcId.ShouldBe(5);

    [Fact]
    public void A_dismissal_holds_only_for_the_same_engine()
    {
        FoodSuggestionRules.IsDismissed("ingredient-parser-nlp 2.10.0", "ingredient-parser-nlp 2.10.0").ShouldBeTrue();
        FoodSuggestionRules.IsDismissed("ingredient-parser-nlp 2.10.0", "ingredient-parser-nlp 2.11.0").ShouldBeFalse();
        FoodSuggestionRules.IsDismissed(null, "ingredient-parser-nlp 2.10.0").ShouldBeFalse();
    }

    [Fact]
    public void Nothing_to_pick_from_is_nothing() => FoodSuggestionRules.Pick([], _ => true).ShouldBeNull();
}
