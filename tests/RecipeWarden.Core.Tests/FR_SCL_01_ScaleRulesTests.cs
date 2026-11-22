using RecipeWarden.Core.Measurement;
using static RecipeWarden.Core.Tests.MeasurementTestData;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-SCL-01, FR-SCL-03, BR-20, BR-29, AC-30, ADR 0014: presets, custom factor and target servings.</summary>
[Trait("Req", "FR-SCL-01")]
public sealed class FR_SCL_01_ScaleRulesTests
{
    [Fact]
    public void Presets_are_the_spec_values()
    {
        ScaleRules.Presets.ShouldBe([0.5m, 1m, 1.5m, 2m, 3m]);
    }

    [Theory]
    [Trait("Req", "AC-30")]
    [InlineData("1.5", 1.5)]
    [InlineData("2", 2)]
    [InlineData("10", 10)]
    [InlineData("10x", 10)]
    [InlineData("1/2", 0.5)]
    [InlineData("1 1/2", 1.5)]
    [InlineData("0.1", 0.1)]
    [InlineData("100", 100)]
    public void Custom_factor_is_read(string text, double expected)
    {
        var choice = ScaleRules.Parse(text, null, 4m);
        choice.Factor.ShouldBe((decimal)expected);
        choice.Error.ShouldBeNull();
    }

    [Theory]
    [InlineData("0.05")]
    [InlineData("101")]
    [InlineData("0")]
    public void Factor_outside_0_1_to_100_falls_back_to_1x_with_a_message(string text)
    {
        var choice = ScaleRules.Parse(text, null, 4m);
        choice.Factor.ShouldBe(1m);
        choice.Error.ShouldBe(ScaleRules.OutOfRange);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-2")]
    [InlineData("1e5")]
    [InlineData("2/0")]
    [InlineData("1 2 3")]
    [InlineData("123456789012345678901")]
    public void Unreadable_factor_falls_back_to_1x_with_a_message(string text)
    {
        var choice = ScaleRules.Parse(text, null, 4m);
        choice.Factor.ShouldBe(1m);
        choice.Error.ShouldBe(ScaleRules.NotANumber);
    }

    [Fact]
    public void No_scale_is_1x_without_a_message()
    {
        var choice = ScaleRules.Parse(null, "  ", 4m);
        choice.ShouldBe(new ScaleChoice(1m, null, null));
        choice.IsScaled.ShouldBeFalse();
    }

    [Theory]
    [Trait("Req", "AC-30")]
    [InlineData(6, 1.5)]
    [InlineData(8, 2)]
    [InlineData(40, 10)]
    [InlineData(2, 0.5)]
    public void Target_servings_sets_the_factor(int target, double factor)
    {
        var choice = ScaleRules.Parse(null, target.ToString(System.Globalization.CultureInfo.InvariantCulture), 4m);
        choice.Factor.ShouldBe((decimal)factor);
        choice.TargetServings.ShouldBe(target);
    }

    [Fact]
    public void Target_servings_wins_over_scale()
    {
        ScaleRules.Parse("3", "8", 4m).Factor.ShouldBe(2m);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("0")]
    public void Bad_target_servings_falls_back(string servings)
    {
        var choice = ScaleRules.Parse(null, servings, 4m);
        choice.Factor.ShouldBe(1m);
        choice.Error.ShouldBe(ScaleRules.ServingsNotANumber);
    }

    [Fact]
    public void Target_servings_beyond_100x_falls_back()
    {
        ScaleRules.Parse(null, "500", 4m).Error.ShouldBe(ScaleRules.OutOfRange);
    }

    [Theory]
    [Trait("Req", "AC-30")]
    [InlineData(1.5, 6)]
    [InlineData(2, 8)]
    [InlineData(10, 40)]
    public void Servings_are_multiplied(double factor, int expected)
    {
        ScaleRules.Servings(4m, null, (decimal)factor).ShouldBe((expected, (decimal?)null));
    }

    [Fact]
    public void Servings_range_scales_both_ends()
    {
        ScaleRules.Servings(6m, 8m, 1.5m).ShouldBe((9m, (decimal?)12m));
    }

    [Theory]
    [Trait("Req", "BR-29")]
    [InlineData(2, false)]
    [InlineData(2.5, true)]
    [InlineData(3, true)]
    [InlineData(0.5, false)]
    public void Time_note_above_2x(double factor, bool expected)
    {
        (ScaleRules.NoteFor((decimal)factor) is not null).ShouldBe(expected);
    }

    [Theory]
    [InlineData(1.5, "1.5x", "1.5")]
    [InlineData(10, "10x", "10")]
    [InlineData(0.6666666, "0.67x", "0.6667")]
    public void Labels_and_query_values(double factor, string label, string query)
    {
        ScaleRules.Label((decimal)factor).ShouldBe(label);
        ScaleRules.ToQuery((decimal)factor).ShouldBe(query);
    }

    [Fact]
    [Trait("Req", "AC-30")]
    public void Every_scalable_amount_is_multiplied_and_others_are_left_alone()
    {
        Scaled(Line(2m, "cup", "milk"), 2m).ShouldBe("4 cups milk");
        Scaled(Line(1m, "lb", "ground beef"), 1.5m).ShouldBe("1½ lb ground beef");
        Scaled(Line(1m, "tbsp", "olive oil"), 10m).ShouldBe("⅔ cup olive oil"); // BR-21: one fill within ±8%
        Scaled(Line(null, null, "Salt and pepper", note: "to taste"), 10m).ShouldBe("Salt and pepper, to taste");
        Scaled(Line(null, null, "fresh parsley", prep: "for garnish"), 2m).ShouldBe("fresh parsley, for garnish");
    }

    [Fact]
    [Trait("Req", "AC-30")]
    public void Range_amounts_scale_both_ends()
    {
        Scaled(Line(2m, "tbsp", "water", max: 3m), 2m).ShouldBe("4–6 Tbsp water");
    }

    [Fact]
    [Trait("Req", "FR-SCL-03")]
    public void Scaling_leaves_the_input_unchanged()
    {
        var line = Line(0.75m, "cup", "sugar");
        var before = line with { };
        _ = Scaled(line, 3m, DisplayUnits.Metric);
        line.ShouldBe(before);
    }

    [Fact]
    public void Lines_that_need_review_are_not_scaled_and_say_so()
    {
        var line = Line(2m, "cup", "something odd") with { RawText = "2 cups of something odd", Confidence = 0.5m };
        var scaled = IngredientScaler.Scale(new ScaleInput(line), Context(2m));
        scaled.Text.ShouldBe("2 cups of something odd");
        scaled.Hint.ShouldBe(IngredientScaler.NeedsCheckingHint);
    }

    [Fact]
    public void At_1x_as_written_the_line_matches_the_reading_view()
    {
        var line = Line(1.5m, "cup", "flour", prep: "sifted", note: "plus 2 Tbsp");
        Scaled(line, 1m).ShouldBe(RecipeWarden.Core.Recipes.IngredientFormatter.Format(line));
    }

    [Fact]
    public void Plus_amount_in_the_note_scales_with_the_line()
    {
        var line = Line(1m, "cup", "flour", note: "plus 2 tablespoons");
        var plus = Line(2m, "tbsp", "x");
        var scaled = IngredientScaler.Scale(new ScaleInput(line, null, plus), Context(2m));
        scaled.Text.ShouldBe("2¼ cups flour");
    }
}
