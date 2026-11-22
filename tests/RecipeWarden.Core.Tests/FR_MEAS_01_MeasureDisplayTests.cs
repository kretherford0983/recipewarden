using RecipeWarden.Core.Measurement;
using static RecipeWarden.Core.Tests.MeasurementTestData;

namespace RecipeWarden.Core.Tests;

/// <summary>
/// FR-MEAS-01, BR-23, BR-24, BR-28 (ADR 0012): unit choice, escalation, fractions and conversion between systems
/// and between volume and weight. Tool combinations are the M3 planner's.
/// </summary>
[Trait("Req", "FR-MEAS-01")]
public sealed class FR_MEAS_01_MeasureDisplayTests
{
    [Theory]
    [Trait("Req", "BR-28")]
    [InlineData(1.125, "1⅛")]
    [InlineData(0.0625, "1/16")]
    [InlineData(2.1875, "2 3/16")]
    [InlineData(0.5, "½")]
    [InlineData(3, "3")]
    [InlineData(0.625, "⅝")]
    public void Fractions_use_glyphs_where_they_exist(double value, string expected)
    {
        Fractions.Format((decimal)value).ShouldBe(expected);
    }

    [Fact]
    [Trait("Req", "BR-28")]
    public void Thirds_round_trip_to_a_glyph()
    {
        Fractions.Format(Fractions.Nearest(0.33m, Fractions.Standard).Value).ShouldBe("⅓");
    }

    [Fact]
    [Trait("Req", "BR-28")]
    public void Only_br28_denominators_are_used()
    {
        Fractions.Nearest(0.2m, Fractions.Standard).Value.ShouldBe(0.25m);
        Fractions.Nearest(0.06m, Fractions.Teaspoon).Value.ShouldBe(0.0625m);
        Fractions.Nearest(0.01m, Fractions.Standard).Value.ShouldBe(0.125m); // never rounds to zero
    }

    [Theory]
    [Trait("Req", "BR-28")]
    [InlineData(9.44, "g", "9.4")]
    [InlineData(12.6, "g", "13")]
    [InlineData(236.5, "ml", "237")]
    [InlineData(0.62, "ml", "0.6")]
    [InlineData(1.234, "kg", "1.23")]
    public void Metric_decimals(double value, string unit, string expected)
    {
        Fractions.FormatMetric((decimal)value, unit).ShouldBe(expected);
    }

    [Fact]
    public void Three_quarter_cup_at_1_5x_is_one_and_an_eighth_cups()
    {
        // AC-32's exact amount; the "1 cup + 2 Tbsp" tool plan arrives with the M3 planner (ADR 0012).
        Scaled(Line(0.75m, "cup", "sugar"), 1.5m).ShouldBe("1⅛ cups sugar");
    }

    [Theory]
    [Trait("Req", "BR-24")]
    [InlineData(8, "tbsp", 2, "1 cup butter")]
    [InlineData(12, "tsp", 4, "1 cup butter")]
    [InlineData(4, "tsp", 3, "¼ cup butter")]
    [InlineData(2, "tsp", 2, "4 tsp butter")]
    [InlineData(3, "tbsp", 2, "6 Tbsp butter")]
    [InlineData(12, "oz", 2, "1½ lb butter")]
    public void Amounts_move_up_a_unit_only_at_a_common_measure(int qty, string unit, int factor, string expected)
    {
        Scaled(Line(qty, unit, "butter"), factor).ShouldBe(expected);
    }

    [Fact]
    public void Small_amounts_move_down_a_unit()
    {
        Scaled(Line(0.25m, "cup", "lemon juice"), 0.25m).ShouldBe("1 Tbsp lemon juice");
        Scaled(Line(1m, "tbsp", "lemon juice"), 0.25m).ShouldBe("¾ tsp lemon juice");
    }

    [Fact]
    [Trait("Req", "FR-REC-06")]
    public void Precision_recipes_use_the_tighter_tolerance()
    {
        // 0.6 cup: ⅝ cup is 4% off, fine at ±8%, too far at ±2% (BR-21).
        Scaled(Line(0.4m, "cup", "milk"), 1.5m).ShouldBe("⅝ cup milk");
        Scaled(Line(0.4m, "cup", "milk"), 1.5m, precision: true).ShouldBe("9½ Tbsp milk");
    }

    [Fact]
    public void Fluid_ounces_pints_and_quarts_stay_in_their_unit()
    {
        Scaled(Line(2m, "quart", "chicken broth"), 2m).ShouldBe("4 quarts chicken broth");
        Scaled(Line(12m, "floz", "beer"), 1.5m).ShouldBe("18 fl oz beer");
    }

    [Theory]
    [InlineData(1, "cup", "milk", "237 ml milk")]
    [InlineData(2, "lb", "beef", "907 g beef")]
    [InlineData(3, "lb", "beef", "1.36 kg beef")]
    [InlineData(0.125, "tsp", "salt", "0.6 ml salt")]
    public void Metric_view_converts_us_amounts(double qty, string unit, string name, string expected)
    {
        Scaled(Line((decimal)qty, unit, name), 1m, DisplayUnits.Metric).ShouldBe(expected);
    }

    [Theory]
    [InlineData(250, "ml", "milk", "1 cup milk")]
    [InlineData(500, "g", "flour", "1⅛ lb flour")]
    [InlineData(15, "ml", "oil", "1 Tbsp oil")]
    public void Us_view_converts_metric_amounts(int qty, string unit, string name, string expected)
    {
        Scaled(Line(qty, unit, name), 1m, DisplayUnits.US).ShouldBe(expected);
    }

    [Fact]
    public void Metric_amounts_stay_metric_as_written()
    {
        Scaled(Line(200m, "g", "butter"), 1.5m).ShouldBe("300 g butter");
        Scaled(Line(800m, "g", "flour"), 1.5m).ShouldBe("1.2 kg flour");
    }

    [Fact]
    [Trait("Req", "BR-22")]
    public void Weight_view_uses_the_density_and_keeps_the_volume_as_a_secondary_line()
    {
        var scaled = IngredientScaler.Scale(new ScaleInput(Line(2m, "cup", "all-purpose flour"), Flour), Context(1m, DisplayUnits.Weight));
        scaled.Text.ShouldBe("250 g all-purpose flour");
        scaled.Secondary.ShouldBe("2 cups");
        scaled.Hint.ShouldBeNull();
    }

    [Fact]
    [Trait("Req", "BR-23")]
    public void Without_a_density_the_original_volume_is_kept_and_no_guess_is_made()
    {
        var scaled = IngredientScaler.Scale(new ScaleInput(Line(2m, "cup", "mystery flour")), Context(1m, DisplayUnits.Weight));
        scaled.Text.ShouldBe("2 cups mystery flour");
        scaled.Secondary.ShouldBeNull();
        scaled.Hint.ShouldBe(IngredientScaler.NoDensityHint);
    }

    [Fact]
    public void Weight_view_shows_us_weights_in_grams()
    {
        Scaled(Line(1m, "lb", "ground beef"), 1m, DisplayUnits.Weight).ShouldBe("454 g ground beef");
    }

    [Fact]
    public void Converting_there_and_back_gives_the_original_amount()
    {
        foreach (var (qty, unit) in new[] { (1m, "cup"), (0.5m, "cup"), (2m, "tbsp"), (1m, "tsp"), (1m, "lb"), (8m, "oz") })
        {
            var u = UnitCatalog.FindByCode(unit)!;
            var metric = MeasureDisplay.Show(qty * u.ToBase, u.Kind, UnitSystem.Metric, null, 0.08m);
            var back = MeasureDisplay.Show(metric.Value * metric.Unit.ToBase, u.Kind, UnitSystem.US, null, 0.08m);
            (back.Value * back.Unit.ToBase).ShouldBe(qty * u.ToBase, qty * u.ToBase * 0.03m);
        }
    }

    [Fact]
    public void Edited_unit_values_from_the_catalog_are_used()
    {
        var edited = new MeasurementCatalog(units: [UnitCatalog.FindByCode("pinch")! with { ToBase = 0.61611519921875m }]);
        edited.Unit("pinch")!.ToBase.ShouldBe(0.61611519921875m);
        edited.Unit("cup").ShouldBe(UnitCatalog.FindByCode("cup"));
    }
}
