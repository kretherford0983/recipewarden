using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>BR-03: servings are positive; unknown servings default to 4 and are flagged estimated.</summary>
[Trait("Req", "BR-03")]
public sealed class BR_03_ServingsRulesTests
{
    [Fact]
    public void Unknown_servings_default_to_four_estimated()
    {
        var result = ServingsRules.Resolve(null);

        result.IsValid.ShouldBeTrue();
        result.Servings.ShouldBe(4m);
        result.Estimated.ShouldBeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(0.5)]
    [InlineData(1000)]
    public void Positive_servings_are_kept_and_not_estimated(double servings)
    {
        var result = ServingsRules.Resolve((decimal)servings);

        result.IsValid.ShouldBeTrue();
        result.Servings.ShouldBe((decimal)servings);
        result.Estimated.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Zero_or_negative_servings_are_refused(double servings)
    {
        var result = ServingsRules.Resolve((decimal)servings);

        result.IsValid.ShouldBeFalse();
        result.Error.ShouldBe("Servings must be a positive number");
    }

    [Fact]
    public void Absurd_servings_are_refused()
    {
        ServingsRules.Resolve(ServingsRules.Maximum + 1).IsValid.ShouldBeFalse();
    }
}
