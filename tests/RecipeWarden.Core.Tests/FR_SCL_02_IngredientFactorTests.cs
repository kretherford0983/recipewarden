using RecipeWarden.Core.Measurement;
using static RecipeWarden.Core.Tests.MeasurementTestData;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-SCL-02, AC-31: scale from the amount of one ingredient on hand.</summary>
[Trait("Req", "FR-SCL-02")]
public sealed class FR_SCL_02_IngredientFactorTests
{
    [Fact]
    [Trait("Req", "AC-31")]
    public void Three_pounds_on_hand_for_a_recipe_calling_for_one_and_a_half_gives_2x()
    {
        var result = IngredientFactor.Compute(Line(3m, "lb", "chicken"), Line(1.5m, "lb", "chicken"), null);
        result.ShouldBe(new IngredientFactorResult(2m, null));
    }

    [Fact]
    [Trait("Req", "AC-31")]
    public void Different_units_of_the_same_kind_compare()
    {
        IngredientFactor.Compute(Line(48m, "oz", "chicken"), Line(1.5m, "lb", "chicken"), null).Factor.ShouldBe(2m);
        IngredientFactor.Compute(Line(1m, "cup", "milk"), Line(8m, "tbsp", "milk"), null).Factor.ShouldBe(2m);
    }

    [Fact]
    public void Weight_and_volume_compare_with_a_density()
    {
        var result = IngredientFactor.Compute(Line(500m, "g", "flour"), Line(2m, "cup", "flour"), 0.5283m);
        result.Factor!.Value.ShouldBe(2m, 0.001m);
    }

    [Fact]
    [Trait("Req", "BR-23")]
    public void Weight_and_volume_without_a_density_are_refused()
    {
        IngredientFactor.Compute(Line(500m, "g", "mystery"), Line(2m, "cup", "mystery"), null)
            .ShouldBe(new IngredientFactorResult(null, IngredientFactor.NoDensity));
    }

    [Fact]
    public void Counts_compare_with_counts()
    {
        IngredientFactor.Compute(Line(6m, null, "eggs"), Line(3m, null, "eggs"), null).Factor.ShouldBe(2m);
        IngredientFactor.Compute(Line(6m, null, "chicken"), Line(1.5m, "lb", "chicken"), null).Error.ShouldBe(IngredientFactor.DifferentKinds);
        IngredientFactor.Compute(Line(2m, "can", "beans"), Line(1m, "can", "beans"), null).Factor.ShouldBe(2m);
    }

    [Fact]
    public void A_range_uses_its_low_end()
    {
        IngredientFactor.Compute(Line(3m, "lb", "chicken"), Line(1m, "lb", "chicken", max: 2m), null).Factor.ShouldBe(3m);
    }

    [Fact]
    public void Missing_amounts_and_out_of_range_factors_explain_why()
    {
        IngredientFactor.Compute(Line(3m, "lb", "x"), Line(null, null, "x"), null).Error.ShouldBe(IngredientFactor.NoAmount);
        IngredientFactor.Compute(Line(null, null, "x"), Line(1m, "lb", "x"), null).Error.ShouldBe(IngredientFactor.HaveNoAmount);
        IngredientFactor.Compute(Line(1000m, "lb", "x"), Line(1m, "lb", "x"), null).Error.ShouldBe(IngredientFactor.OutOfRange);
    }
}
