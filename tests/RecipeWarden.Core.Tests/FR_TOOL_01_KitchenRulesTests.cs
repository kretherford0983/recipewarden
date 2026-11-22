using RecipeWarden.Core.Kitchen;
using RecipeWarden.Core.Measurement;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-TOOL-01/02, BR-33, ADR 0021: tools, their checks, the standard set and the presets.</summary>
[Trait("Req", "FR-TOOL-01")]
public sealed class FR_TOOL_01_KitchenRulesTests
{
    [Fact]
    [Trait("Req", "BR-33")]
    public void The_standard_set_is_br_33()
    {
        KitchenRules.StandardSet.Select(KitchenRules.Describe)
            .ShouldBe(["¼ tsp", "½ tsp", "1 tsp", "1 Tbsp", "¼ cup", "⅓ cup", "½ cup", "1 cup"]);
        KitchenRules.StandardSet.ShouldAllBe(t => KitchenRules.Validate(t).Count == 0);
    }

    [Fact]
    public void Every_preset_is_a_valid_tool()
    {
        KitchenRules.Presets.SelectMany(p => p.Tools).ShouldAllBe(t => KitchenRules.Validate(t).Count == 0);
        KitchenRules.Presets.Select(p => p.Key).Distinct().Count().ShouldBe(KitchenRules.Presets.Count);
    }

    [Theory]
    [InlineData(ToolType.MeasuringSpoon, null, "tsp")]
    [InlineData(ToolType.MeasuringSpoon, 3.0, "tbsp")]
    [InlineData(ToolType.Container, 16.0, "oz")]
    [InlineData(ToolType.MeasuringCup, 0.0, "cup")]
    public void Measures_and_containers_need_a_volume_capacity(ToolType type, double? amount, string unit)
    {
        KitchenRules.Validate(new KitchenToolInfo(0, "x", type, (decimal?)amount, unit)).ShouldNotBeEmpty();
    }

    [Fact]
    public void Scales_need_a_resolution_and_units()
    {
        KitchenRules.Validate(new KitchenToolInfo(0, "Scale", ToolType.Scale)).Count.ShouldBe(2);
        KitchenRules.Validate(new KitchenToolInfo(0, "Scale", ToolType.Scale, Resolution: 1m, ResolutionUnit: "g", SupportedUnits: ["lb"])).ShouldNotBeEmpty();
    }

    [Fact]
    public void Pans_need_a_shape_and_size()
    {
        KitchenRules.Validate(KitchenRules.Pan("p", PanShape.Rectangle, 9m, 0m)).ShouldNotBeEmpty();
        KitchenRules.Validate(KitchenRules.Pan("p", PanShape.Round, 9m, 0m)).ShouldBeEmpty();
        KitchenRules.Validate(KitchenRules.Pan("p", PanShape.Rectangle, 9m, 40m)).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("", 1, 100)]
    [InlineData("ok", 0, 100)]
    [InlineData("ok", 1, 0)]
    [InlineData("ok", 100, 50)]
    public void Name_quantity_and_fill_are_checked(string name, int qty, int fill)
    {
        KitchenRules.Validate(new KitchenToolInfo(0, name, ToolType.Appliance, QuantityOwned: qty, DefaultFillPct: fill)).ShouldNotBeEmpty();
    }

    [Fact]
    public void Appliances_and_thermometers_need_only_a_name()
    {
        KitchenRules.Validate(new KitchenToolInfo(0, "Instant Pot", ToolType.Appliance)).ShouldBeEmpty();
        KitchenRules.Validate(new KitchenToolInfo(0, "Probe thermometer", ToolType.Thermometer)).ShouldBeEmpty();
    }

    [Fact]
    public void Descriptions()
    {
        KitchenRules.Describe(KitchenRules.Spoon(0.125m, "tsp")).ShouldBe("⅛ tsp");
        KitchenRules.Describe(KitchenRules.Container("b", 16m, "floz", 90m)).ShouldBe("16 fl oz");
        KitchenRules.Describe(KitchenRules.Pan("p", PanShape.Rectangle, 9m, 13m)).ShouldBe("9x13");
        KitchenRules.Describe(KitchenRules.Presets.Single(p => p.Key == "scale").Tools[0]).ShouldBe("1 g steps; shows g, oz");
        KitchenRules.Spoon(0.125m, "tsp").CapacityMl.ShouldBe(UnitCatalog.FindByCode("tsp")!.ToBase / 8m);
    }
}
