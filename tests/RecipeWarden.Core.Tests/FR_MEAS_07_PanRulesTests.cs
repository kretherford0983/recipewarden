using RecipeWarden.Core.Kitchen;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-MEAS-07 (pan part), BR-29, ADR 0020: pan sizes read, found in steps, and matched by area.</summary>
[Trait("Req", "FR-MEAS-07")]
public sealed class FR_MEAS_07_PanRulesTests
{
    [Theory]
    [InlineData("9x13", PanShape.Rectangle, 9, 13)]
    [InlineData("13 x 9 inch", PanShape.Rectangle, 9, 13)]
    [InlineData("9-inch round", PanShape.Round, 9, 9)]
    [InlineData("9 inch springform", PanShape.Round, 9, 9)]
    [InlineData("8x8 square", PanShape.Square, 8, 8)]
    [InlineData("8x8", PanShape.Square, 8, 8)]
    [InlineData("9x5 loaf", PanShape.Loaf, 5, 9)]
    [InlineData("half sheet", PanShape.Rectangle, 13, 18)]
    [InlineData("8-inch square", PanShape.Square, 8, 8)]
    public void Pan_text_is_read(string text, PanShape shape, int width, int length)
    {
        PanRules.Parse(text).ShouldBe(new Pan(shape, width, length));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("a big pan")]
    [InlineData("40x40")]
    public void Unreadable_or_impossible_pans_are_null(string? text)
    {
        PanRules.Parse(text).ShouldBeNull();
    }

    [Theory]
    [InlineData("Preheat oven. Grease a 9x13-inch baking dish.", "9x13")]
    [InlineData("Pour into a 9-inch round cake pan and bake.", "9-inch round")]
    [InlineData("Spread on a half-sheet pan.", "13x18")]
    [InlineData("Line an 8x8 pan with parchment.", "8x8 square")]
    public void The_pan_is_found_in_the_steps(string steps, string expected)
    {
        PanRules.Describe(PanRules.FromSteps(steps).ShouldNotBeNull()).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Bake 25 minutes at 350°F.")]
    [InlineData("Add 2 x 3 tablespoons of butter.")]
    [InlineData(null)]
    public void Steps_without_a_pan_give_none(string? steps)
    {
        PanRules.FromSteps(steps).ShouldBeNull();
    }

    [Fact]
    public void Doubling_a_9x13_suggests_a_half_sheet_or_two_9x13s()
    {
        var options = PanRules.Alternatives(new Pan(PanShape.Rectangle, 9m, 13m), 2m, []);
        options.Select(o => o.Text).ShouldContain("half sheet (13x18)");
        options.Select(o => o.Text).ShouldContain("2 × 9x13 rectangle");
        options.ShouldAllBe(o => o.Error <= PanRules.Tolerance);
    }

    [Fact]
    public void Owned_pans_come_first()
    {
        var mine = new[] { ("My 9x13", new Pan(PanShape.Rectangle, 9m, 13m)) };
        var options = PanRules.Alternatives(new Pan(PanShape.Rectangle, 9m, 13m), 2m, mine);
        options[0].Text.ShouldBe("2 × My 9x13 (in My Kitchen)");
        options.ShouldNotContain(o => o.Name == "9x13 rectangle"); // the standard one is the same size
    }

    [Fact]
    public void Halving_a_9x13_suggests_an_8x8()
    {
        PanRules.Alternatives(new Pan(PanShape.Rectangle, 9m, 13m), 0.5m, []).Select(o => o.Name).ShouldContain("8x8 square");
    }

    [Fact]
    public void Round_pans_use_circle_area_and_1x_has_no_alternatives()
    {
        new Pan(PanShape.Round, 9m, 9m).Area.ShouldBe(63.617m, 0.01m);
        PanRules.Alternatives(new Pan(PanShape.Round, 9m, 9m), 1m, []).ShouldBeEmpty();
    }
}
