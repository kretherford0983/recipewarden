using RecipeWarden.Core.Kitchen;
using RecipeWarden.Core.Measurement;
using static RecipeWarden.Core.Tests.MeasurementTestData;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-SCL-04/05/06, BR-110 to BR-114, AC-37, AC-38, AC-39, ADR 0019: scaling to fill containers.</summary>
[Trait("Req", "FR-SCL-04")]
public sealed class FR_SCL_04_ContainerRulesTests
{
    private static readonly UnitDefinition FlOz = UnitCatalog.FindByCode("floz")!;

    private static ContainerChoice Bottle(int count = 1, int? owned = null) =>
        new("bottle", 16m * FlOz.ToBase, FlOz, 90m, count, owned);

    private static FinishedYield Sauce => ContainerRules.Stored(12m, "floz", YieldSource.Measured, null, out _)!;

    [Fact]
    [Trait("Req", "AC-37")]
    public void A_12_fl_oz_sauce_fits_a_16_oz_bottle_at_90_percent_with_1_2x()
    {
        var fit = ContainerRules.Fit(Sauce, Bottle());
        fit.Factor.ShouldBe(1.2m);
        fit.Fill.ShouldBe("fills about 14.4 of 16 fl oz");
        fit.Error.ShouldBeNull();
    }

    [Fact]
    [Trait("Req", "AC-37")]
    [Trait("Req", "FR-SCL-05")]
    public void Three_bottles_give_3_6x()
    {
        var fit = ContainerRules.Fit(Sauce, Bottle(3));
        fit.Factor.ShouldBe(3.6m);
        fit.Fill.ShouldBe("fills about 14.4 of 16 fl oz in each of 3 bottles");
    }

    [Fact]
    [Trait("Req", "AC-38")]
    public void At_2x_the_sauce_fills_one_bottle_and_two_thirds_of_another()
    {
        ContainerRules.HowMany(Sauce.Ml * 2m, Bottle()).ShouldBe("1 full bottle + about ⅔ of another");
    }

    [Theory]
    [Trait("Req", "FR-SCL-05")]
    [InlineData(0.6, "about ½ of a bottle")]
    [InlineData(1.2, "1 full bottle")]
    [InlineData(3.6, "3 full bottles")]
    [InlineData(3.54, "3 full bottles")]
    public void How_many_containers(double factor, string expected)
    {
        ContainerRules.HowMany(Sauce.Ml * (decimal)factor, Bottle()).ShouldBe(expected);
    }

    [Fact]
    [Trait("Req", "BR-114")]
    public void Asking_for_more_containers_than_owned_is_allowed_with_a_note()
    {
        var fit = ContainerRules.Fit(Sauce, Bottle(3, owned: 2));
        fit.Factor.ShouldBe(3.6m);
        fit.Note.ShouldBe("You have 2; this needs 3.");
    }

    [Fact]
    [Trait("Req", "AC-39")]
    [Trait("Req", "BR-111")]
    public void Without_a_stored_yield_an_estimate_lists_what_it_could_not_include()
    {
        var lines = new[]
        {
            new ScaleInput(Line(1m, "cup", "ketchup")),
            new ScaleInput(Line(2m, "tbsp", "vinegar")),
            new ScaleInput(Line(100m, "g", "butter"), Butter),
            new ScaleInput(Line(2m, "clove", "garlic")),
            new ScaleInput(Line(null, null, "salt", note: "to taste")),
        };

        var estimate = ContainerRules.Estimate(lines, Catalog);

        estimate.IsEstimate.ShouldBeTrue();
        estimate.Ml.ShouldBe(236.588m + 29.574m + 100m / 0.9595m, 0.01m);
        estimate.Excluded.ShouldBe(["garlic"]);
    }

    [Fact]
    [Trait("Req", "BR-112")]
    public void A_weight_yield_needs_a_finished_density()
    {
        ContainerRules.Stored(500m, "g", YieldSource.Entered, null, out var error).ShouldBeNull();
        error.ShouldBe(ContainerRules.WeightYieldNeedsDensity);
        ContainerRules.Stored(500m, "g", YieldSource.Entered, 1.25m, out _)!.Ml.ShouldBe(400m);
    }

    [Theory]
    [Trait("Req", "BR-111")]
    [InlineData(YieldSource.Measured)]
    [InlineData(YieldSource.Entered)]
    public void Stored_yields_keep_their_source(YieldSource source)
    {
        ContainerRules.Stored(2m, "cup", source, null, out _)!.Source.ShouldBe(source);
        (YieldSource.Measured > YieldSource.Entered && YieldSource.Entered > YieldSource.Estimated).ShouldBeTrue();
    }

    [Fact]
    [Trait("Req", "BR-113")]
    public void Rounding_that_moves_the_total_more_than_5_percent_is_warned()
    {
        ContainerRules.RoundingWarning([new ScaledIngredient("a") { ExactMl = 100m, ShownMl = 107m }])
            .ShouldBe("Rounding the amounts puts you about 7% over: the containers may overflow.");
        ContainerRules.RoundingWarning([new ScaledIngredient("a") { ExactMl = 100m, ShownMl = 93m }])!.ShouldContain("7% under");
        ContainerRules.RoundingWarning([new ScaledIngredient("a") { ExactMl = 100m, ShownMl = 104m }]).ShouldBeNull();
    }

    [Fact]
    public void Bad_input_explains_instead_of_guessing()
    {
        ContainerRules.Fit(new FinishedYield(0m, YieldSource.Estimated, []), Bottle()).Error.ShouldBe(ContainerRules.NoYield);
        ContainerRules.Fit(Sauce, Bottle() with { FillPct = 0m }).Error.ShouldNotBeNull();
        ContainerRules.Fit(new FinishedYield(1m, YieldSource.Entered, []), Bottle(99)).Error.ShouldBe(ScaleRules.OutOfRange);
    }

    [Theory]
    [InlineData("16 oz squeeze bottle", "bottle")]
    [InlineData("Pint jar", "jar")]
    [InlineData("1 qt freezer containers", "container")]
    [InlineData(null, "container")]
    public void Container_nouns(string? name, string noun)
    {
        ContainerRules.Noun(name).ShouldBe(noun);
    }
}
