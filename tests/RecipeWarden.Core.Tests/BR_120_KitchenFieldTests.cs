using RecipeWarden.Core.Kitchen;

namespace RecipeWarden.Core.Tests;

/// <summary>BR-120, BR-121, AC-113, AC-114: each tool type has its own fields; a scale's step is read in grams.</summary>
[Trait("Req", "FR-TOOL-01")]
[Trait("Req", "BR-120")]
public sealed class BR_120_KitchenFieldTests
{
    [Theory]
    [InlineData(ToolType.MeasuringSpoon, true, false, false)]
    [InlineData(ToolType.MeasuringCup, true, false, false)]
    [InlineData(ToolType.LiquidMeasure, true, false, false)]
    [InlineData(ToolType.Container, true, false, true)]
    [InlineData(ToolType.Scale, true, true, false)]
    [InlineData(ToolType.Pan, true, false, false)]
    [InlineData(ToolType.Thermometer, false, false, false)]
    [InlineData(ToolType.Appliance, false, false, false)]
    public void Each_type_has_its_own_fields(ToolType type, bool size, bool units, bool fill)
    {
        var fields = KitchenRules.FieldsFor(type);
        fields.Size.ShouldBe(size);
        fields.ScaleUnits.ShouldBe(units);
        fields.Fill.ShouldBe(fill);
    }

    [Fact]
    public void The_scale_size_field_explains_the_label_on_the_scale()
    {
        var fields = KitchenRules.FieldsFor(ToolType.Scale);
        fields.SizeLabel.ShouldBe("Resolution (smallest step)");
        fields.SizeHint.ShouldContain("Max 14000g x 1g");
        fields.SizePlaceholder.ShouldBe("1 g");
    }

    [Fact]
    [Trait("Req", "AC-113")]
    public void Fields_that_do_not_belong_to_the_type_are_cleared()
    {
        var (spoon, errors) = KitchenRules.FromInput(ToolType.MeasuringSpoon, null, "1/8 tsp", ["g", "oz"], 1, 40m);
        errors.ShouldBeEmpty();
        spoon.DefaultFillPct.ShouldBe(100m);
        spoon.SupportedUnits.ShouldBeNull();
        spoon.Resolution.ShouldBeNull();

        var (pan, panErrors) = KitchenRules.FromInput(ToolType.Pan, "Big pan", "9x13", ["g"], 1, 55m);
        panErrors.ShouldBeEmpty();
        pan.SupportedUnits.ShouldBeNull();
        pan.DefaultFillPct.ShouldBe(100m);
        pan.CapacityAmount.ShouldBeNull();

        var (jar, _) = KitchenRules.FromInput(ToolType.Container, "Jar", "1 quart", ["g"], 2, 85m);
        jar.DefaultFillPct.ShouldBe(85m);
        jar.SupportedUnits.ShouldBeNull();
    }

    [Fact]
    public void A_tool_built_by_hand_keeps_only_its_type_fields()
    {
        var mixed = new KitchenToolInfo(0, "Odd", ToolType.Appliance, 2m, "cup", 1m, "g", ["g"], 1, 50m, PanShape.Round, 9m, 9m);
        var clean = KitchenRules.OnlyItsFields(mixed);
        clean.ShouldBe(new KitchenToolInfo(0, "Odd", ToolType.Appliance, DefaultFillPct: 100m));
    }

    [Theory]
    [Trait("Req", "AC-114")]
    [Trait("Req", "BR-121")]
    [InlineData("1", 1.0, "g")]
    [InlineData("1 g", 1.0, "g")]
    [InlineData("0.1 oz", 0.1, "oz")]
    [InlineData("0.5", 0.5, "g")]
    public void A_scale_step_is_read_with_grams_as_the_default_unit(string size, double step, string unit)
    {
        var (scale, errors) = KitchenRules.FromInput(ToolType.Scale, "Scale", size, ["g", "oz"], 1, 100m);
        errors.ShouldBeEmpty();
        scale.Resolution.ShouldBe((decimal)step);
        scale.ResolutionUnit.ShouldBe(unit);
    }

    [Theory]
    [Trait("Req", "AC-114")]
    [Trait("Req", "BR-121")]
    [InlineData("60 g")]
    [InlineData("14000")]
    [InlineData("2 oz")]
    public void A_scale_step_over_50_g_is_refused(string size)
    {
        var (_, errors) = KitchenRules.FromInput(ToolType.Scale, "Scale", size, ["g"], 1, 100m);
        errors.ShouldContain(e => e.Contains("at most 50 g", StringComparison.Ordinal));
    }

    [Fact]
    public void Resolution_in_grams_converts_ounces()
    {
        KitchenRules.ResolutionGrams(new KitchenToolInfo(0, "s", ToolType.Scale, Resolution: 0.1m, ResolutionUnit: "oz"))!.Value
            .ShouldBe(2.835m, 0.001m);
        KitchenRules.ResolutionGrams(new KitchenToolInfo(0, "s", ToolType.Scale, Resolution: 1m, ResolutionUnit: "lb")).ShouldBeNull();
        KitchenRules.ResolutionGrams(new KitchenToolInfo(0, "s", ToolType.Scale)).ShouldBeNull();
    }
}
