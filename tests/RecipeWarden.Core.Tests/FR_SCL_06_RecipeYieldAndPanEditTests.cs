using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-SCL-06, BR-112, ADR 0019, ADR 0020: the editor's yield, finished density and pan fields.</summary>
[Trait("Req", "FR-SCL-06")]
public sealed class FR_SCL_06_RecipeYieldAndPanEditTests
{
    [Theory]
    [InlineData("12 fl oz", 12, "floz")]
    [InlineData("about 2 1/2 cups", 2.5, "cup")]
    [InlineData("1 quart", 1, "quart")]
    [InlineData("500 g", 500, "g")]
    [InlineData("1.5 l", 1.5, "l")]
    public void Amounts_are_read(string text, double value, string unit)
    {
        AmountText.TryParse(text, out var amount).ShouldBeTrue();
        amount!.Value.ShouldBe((decimal)value);
        amount.Unit.Code.ShouldBe(unit);
    }

    [Theory]
    [InlineData("12 cans")]
    [InlineData("lots")]
    [InlineData("12")]
    [InlineData("0 cups")]
    [InlineData("a pinch")]
    [InlineData(null)]
    public void Counts_vague_and_bare_numbers_are_refused(string? text)
    {
        AmountText.TryParse(text, out _).ShouldBeFalse();
    }

    [Fact]
    public void Edit_rules_check_yield_density_and_pan()
    {
        new RecipeEdit { Title = "x", YieldText = "12 fl oz", FinishedDensityGPerMl = 1.1m, PanText = "9x13" }.Validate().ShouldBeEmpty();
        var errors = new RecipeEdit { Title = "x", YieldText = "12 cans", FinishedDensityGPerMl = 50m, PanText = "huge" }.Validate();
        errors.Select(e => e.Field).ShouldBe([RecipeField.Yield, RecipeField.FinishedDensity, RecipeField.Pan]);
    }
}
