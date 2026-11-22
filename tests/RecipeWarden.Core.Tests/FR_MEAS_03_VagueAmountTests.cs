using RecipeWarden.Core.Measurement;
using static RecipeWarden.Core.Tests.MeasurementTestData;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-MEAS-03, BR-25, AC-34: pinch, dash and smidgen; "to taste" never scales.</summary>
[Trait("Req", "FR-MEAS-03")]
public sealed class FR_MEAS_03_VagueAmountTests
{
    private static ScaledIngredient Pinch(decimal factor, DisplayUnits units = DisplayUnits.AsWritten, MeasurementCatalog? catalog = null) =>
        IngredientScaler.Scale(new ScaleInput(Line(1m, "pinch", "salt")), new ScaleContext(factor, units, false, catalog ?? Catalog));

    [Fact]
    [Trait("Req", "AC-34")]
    public void Pinch_scaled_10x_shows_the_nearest_spoon_and_the_pinch_count()
    {
        Pinch(10m).Text.ShouldBe("about ⅝ tsp salt (~10 pinches)");
    }

    [Fact]
    [Trait("Req", "AC-34")]
    public void Salt_to_taste_never_changes()
    {
        var line = Line(null, null, "salt", note: "to taste");
        Scaled(line, 10m).ShouldBe("salt, to taste");
        Scaled(line, 0.5m, DisplayUnits.Metric).ShouldBe("salt, to taste");
    }

    [Fact]
    [Trait("Req", "BR-25")]
    public void Up_to_four_pinches_keep_the_word_and_show_a_hint()
    {
        var scaled = Pinch(3m);
        scaled.Text.ShouldBe("3 pinches salt");
        scaled.Hint.ShouldBe("about 3/16 tsp");
    }

    [Fact]
    [Trait("Req", "BR-25")]
    public void Unscaled_pinch_shows_its_approximate_amount_as_a_hint()
    {
        var scaled = Pinch(1m);
        scaled.Text.ShouldBe("1 pinch salt");
        scaled.Hint.ShouldBe("about 1/16 tsp");
    }

    [Fact]
    public void Dash_and_smidgen_use_their_own_values()
    {
        IngredientScaler.Scale(new ScaleInput(Line(1m, "dash", "hot sauce")), Context(10m)).Text
            .ShouldBe("about 1¼ tsp hot sauce (~10 dashes)");
        IngredientScaler.Scale(new ScaleInput(Line(1m, "smidgen", "nutmeg")), Context(1m)).Hint.ShouldBe("about 1/16 tsp"); // 1/32 tsp: 1/16 is the smallest BR-28 fraction
    }

    [Fact]
    [Trait("Req", "FR-MEAS-05")]
    public void An_edited_vague_value_is_used()
    {
        var edited = new MeasurementCatalog(units: [UnitCatalog.FindByCode("pinch")! with { ToBase = UnitCatalog.FindByCode("tsp")!.ToBase / 8m }]);
        Pinch(10m, catalog: edited).Text.ShouldBe("about 1¼ tsp salt (~10 pinches)");
    }

    [Fact]
    public void Metric_view_shows_ml()
    {
        Pinch(10m, DisplayUnits.Metric).Text.ShouldBe("about 3 ml salt (~10 pinches)");
    }

    [Fact]
    public void Handfuls_have_no_measure_and_only_scale_as_a_count()
    {
        var scaled = IngredientScaler.Scale(new ScaleInput(Line(1m, "handful", "spinach")), Context(10m));
        scaled.Text.ShouldBe("10 handfuls spinach");
        scaled.Hint.ShouldBeNull();
    }
}
