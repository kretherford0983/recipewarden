using RecipeWarden.Core.Import;
using RecipeWarden.Core.Kitchen;
using RecipeWarden.Core.Measurement;
using static RecipeWarden.Core.Tests.MeasurementTestData;

namespace RecipeWarden.Core.Tests;

/// <summary>
/// FR-IMP-04, FR-SCL-06, BR-134, BR-135, AC-140 to AC-142 (owner testing 2026-11-25): yields written in loose words
/// are read into Makes, and when an import says nothing, Makes is prefilled with the BR-111 estimate.
/// </summary>
[Trait("Req", "FR-SCL-06")]
public sealed class BR_134_ImportYieldTests
{
    [Theory]
    [Trait("Req", "AC-140")]
    [Trait("Req", "BR-134")]
    [InlineData("Approximately 1 1/2 cups of sauce", "about 1 1/2 cups")]
    [InlineData("approx. 2 cups", "about 2 cups")]
    [InlineData("Yield: approximately 2 cups of dressing", "about 2 cups")]
    [InlineData("Makes roughly 3 quarts of stock", "about 3 quarts")]
    [InlineData("around 12 fl oz", "about 12 fl oz")]
    [InlineData("About 3/4 cup", "about 3/4 cup")]
    [InlineData("~ 500 ml", "about 500 ml")]
    [InlineData("12 fl oz", "12 fl oz")]
    public void Loose_yield_words_become_an_amount(string value, string yield)
    {
        var (servings, read, remark) = ImportRules.ServingsAndYield([value]);
        servings.ShouldBeNull();
        read.ShouldBe(yield);
        remark.ShouldBeNull();
        AmountText.TryParse(read, out _).ShouldBeTrue();
    }

    [Theory]
    [Trait("Req", "BR-134")]
    [InlineData("Enough for 3–4 lb of wings")]
    [InlineData("about 24 cookies")]
    public void What_is_not_an_amount_is_not_a_yield(string value)
    {
        ImportRules.YieldAmount(value).ShouldBeNull();
    }

    [Fact]
    [Trait("Req", "AC-141")]
    [Trait("Req", "BR-135")]
    public void A_sauce_without_a_yield_gets_an_estimate_in_cups()
    {
        var prefill = ImportYieldRules.Prefill(
        [
            new ScaleInput(Line(1m, "cup", "hot sauce")),
            new ScaleInput(Line(1m / 3m, "cup", "vegetable oil")),
            new ScaleInput(Line(null, null, "salt", note: "to taste")),
        ], Catalog)!;

        prefill.Text.ShouldBe("about 1 1/3 cups");
        prefill.Excluded.ShouldBeEmpty();
        AmountText.TryParse(prefill.Text, out _).ShouldBeTrue();
        ImportYieldRules.Warning(prefill).ShouldBe(
            "Makes was estimated from the ingredients: about 1 1/3 cups. Check it against what the recipe really makes, or clear it.");
    }

    [Fact]
    [Trait("Req", "BR-135")]
    public void The_warning_names_what_was_not_counted_and_small_and_large_amounts_use_other_units()
    {
        var prefill = ImportYieldRules.Prefill(
        [
            new ScaleInput(Line(2m, "tbsp", "lemon juice")),
            new ScaleInput(Line(1m, "tbsp", "olive oil")),
            new ScaleInput(Line(2m, "clove", "garlic")),
        ], Catalog)!;
        prefill.Text.ShouldBe("about 3 Tbsp");
        prefill.Excluded.ShouldBe(["garlic"]);
        ImportYieldRules.Warning(prefill).ShouldContain("Not counted: garlic.");

        ImportYieldRules.Prefill([new ScaleInput(Line(10m, "cup", "stock")), new ScaleInput(Line(2m, "cup", "water"))], Catalog)!
            .Text.ShouldBe("about 3 quarts");
    }

    [Fact]
    [Trait("Req", "AC-142")]
    [Trait("Req", "BR-135")]
    public void No_estimate_when_most_amounts_cannot_be_counted()
    {
        ImportYieldRules.Prefill(
        [
            new ScaleInput(Line(2m, "lb", "chicken wings")),
            new ScaleInput(Line(3m, null, "eggs")),
            new ScaleInput(Line(1m, "cup", "hot sauce")),
        ], Catalog).ShouldBeNull();

        ImportYieldRules.Prefill([new ScaleInput(Line(null, null, "salt", note: "to taste"))], Catalog).ShouldBeNull();
        ImportYieldRules.Prefill([], Catalog).ShouldBeNull();
    }
}
