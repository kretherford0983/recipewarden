using RecipeWarden.Core.Measurement;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-MEAS-05: limits on densities, vague values and yield conversions an editor can enter.</summary>
[Trait("Req", "FR-MEAS-05")]
public sealed class FR_MEAS_05_FoodRulesTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData(0.53, true)]
    [InlineData(1.44, true)]
    [InlineData(0.005, false)]
    [InlineData(53.0, false)]
    public void Density_range(double? density, bool ok)
    {
        (FoodRules.ValidateDensity((decimal?)density) is null).ShouldBe(ok);
    }

    [Theory]
    [InlineData(0.0625, true)]
    [InlineData(0.001, false)]
    [InlineData(4, false)]
    public void Vague_values_in_teaspoons(double tsp, bool ok)
    {
        (FoodRules.ValidateVagueTsp((decimal)tsp) is null).ShouldBe(ok);
    }

    [Theory]
    [InlineData(1.14, 1.43, 0.59, "simmered", true)]
    [InlineData(1.43, 1.14, null, "simmered", false)]
    [InlineData(1.1, 20, null, "simmered", false)]
    [InlineData(1.1, 1.2, 9.0, "simmered", false)]
    [InlineData(1.1, 1.2, null, " ", false)]
    public void Yield_needs_a_low_to_high_range_and_a_basis(double ratio, double max, double? density, string basis, bool ok)
    {
        (FoodRules.ValidateYield((decimal)ratio, (decimal)max, (decimal?)density, basis) is null).ShouldBe(ok);
    }

    [Fact]
    public void Aliases_are_trimmed_unique_and_exclude_the_name()
    {
        FoodRules.ParseAliases(" AP flour, ap flour ,flour,, All-Purpose Flour ", "all-purpose flour").ShouldBe(["AP flour", "flour"]);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("  ", false)]
    [InlineData("()", false)]
    [InlineData("saffron", true)]
    public void Names(string? name, bool ok)
    {
        (FoodRules.ValidateName(name) is null).ShouldBe(ok);
    }
}
