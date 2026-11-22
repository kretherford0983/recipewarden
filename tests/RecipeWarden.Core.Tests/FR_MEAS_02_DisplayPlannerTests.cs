using RecipeWarden.Core.Kitchen;
using RecipeWarden.Core.Measurement;
using static RecipeWarden.Core.Tests.KitchenTestData;
using static RecipeWarden.Core.Tests.MeasurementTestData;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-MEAS-02, BR-21, BR-24, BR-33, AC-32, ADR 0018: amounts made with the spoons and cups in My Kitchen.</summary>
[Trait("Req", "FR-MEAS-02")]
public sealed class FR_MEAS_02_DisplayPlannerTests
{
    private static readonly decimal Cup = UnitCatalog.FindByCode("cup")!.ToBase;

    [Fact]
    [Trait("Req", "AC-32")]
    public void Three_quarter_cup_at_1_5x_is_one_cup_and_two_tablespoons()
    {
        Scale(Line(0.75m, "cup", "sugar"), KitchenTestData.Context(1.5m, KitchenTools.Standard)).Text.ShouldBe("1 cup + 2 Tbsp sugar");
    }

    [Fact]
    [Trait("Req", "BR-33")]
    public void With_no_spoons_or_cups_the_standard_set_is_assumed_and_said()
    {
        var none = KitchenTools.From([Scale()]);
        none.IsAssumedStandard.ShouldBeTrue();
        none.Measures.Count.ShouldBe(8);
        KitchenTools.Standard.IsAssumedStandard.ShouldBeTrue();
        StandardWith().IsAssumedStandard.ShouldBeFalse();
    }

    [Theory]
    [Trait("Req", "BR-21")]
    [InlineData(0.6)]
    [InlineData(0.3)]
    [InlineData(1.7)]
    [InlineData(0.15)]
    [InlineData(2.5)]
    public void Never_more_than_three_fills_and_within_tolerance(double cups)
    {
        foreach (var tolerance in new[] { 0.02m, 0.08m })
        {
            if (DisplayPlanner.Plan((decimal)cups * Cup, KitchenTools.Standard, tolerance) is { } plan)
            {
                plan.FillCount.ShouldBeLessThanOrEqualTo(DisplayPlanner.MaxFills);
                plan.Error.ShouldBeLessThanOrEqualTo(tolerance);
            }
        }
    }

    [Fact]
    public void Near_exact_plans_win_then_the_fewest_fills()
    {
        // 0.6 cup: no 1- or 2-fill plan is within ±2%; ⅓ cup + ¼ cup + 1 tsp is 0.7% off.
        var plan = DisplayPlanner.Plan(0.6m * Cup, KitchenTools.Standard, 0.08m).ShouldNotBeNull();
        plan.Text.ShouldBe("⅓ cup + ¼ cup + 1 tsp");
        plan.Error.ShouldBeLessThan(0.01m);

        // 2 cups: two fills of the 1 cup.
        DisplayPlanner.Plan(2m * Cup, KitchenTools.Standard, 0.08m)!.Text.ShouldBe("2 cups");
    }

    [Fact]
    [Trait("Req", "FR-REC-06")]
    public void When_nothing_is_near_exact_an_ordinary_recipe_takes_a_close_plan_and_a_baking_recipe_falls_back()
    {
        var ml = 1.07m * Cup; // 1 cup is 6.5% off; 1 cup + 1 Tbsp is 0.7% off, so the near-exact plan wins
        DisplayPlanner.Plan(ml, KitchenTools.Standard, 0.08m)!.Text.ShouldBe("1 cup + 1 Tbsp");

        var odd = 0.05m * Cup; // 2.4 tsp: nothing within 2% in 3 fills of the standard set; 2½ tsp is 4% off
        DisplayPlanner.Plan(odd, KitchenTools.Standard, 0.02m).ShouldBeNull();
        DisplayPlanner.Plan(odd, KitchenTools.Standard, 0.08m).ShouldNotBeNull();
    }

    [Fact]
    [Trait("Req", "BR-24")]
    public void Large_amounts_beyond_three_fills_fall_back_to_the_nearest_fraction()
    {
        DisplayPlanner.Plan(5m * Cup, KitchenTools.Standard, 0.08m).ShouldBeNull();
        Scale(Line(2.5m, "cup", "stock"), KitchenTestData.Context(2m, KitchenTools.Standard)).Text.ShouldBe("5 cups stock");
    }

    [Fact]
    [Trait("Req", "AC-40")]
    public void A_new_eighth_teaspoon_is_used()
    {
        var ml = 0.375m * UnitCatalog.FindByCode("tsp")!.ToBase; // ⅜ tsp
        DisplayPlanner.Plan(ml, KitchenTools.Standard, 0.08m).ShouldBeNull();
        var withEighth = DisplayPlanner.Plan(ml, StandardWith(KitchenRules.Spoon(0.125m, "tsp") with { Id = 99 }), 0.02m).ShouldNotBeNull();
        withEighth.Fills.ShouldContain(f => f.Tool.Id == 99);
        withEighth.Text.ShouldBe("⅜ tsp");
    }

    [Fact]
    [Trait("Req", "FR-TOOL-03")]
    public void Retired_tools_are_not_used()
    {
        var tools = KitchenTools.From(KitchenRules.StandardSet.Select((t, i) => t with { Id = i + 1, IsActive = t.CapacityUnit != "tbsp" }));
        tools.Measures.ShouldNotContain(t => t.CapacityUnit == "tbsp");
        DisplayPlanner.Plan(1.125m * Cup, tools, 0.08m)!.Text.ShouldNotContain("Tbsp");
    }

    [Fact]
    public void Plans_explain_the_exact_amount_and_the_error()
    {
        var scaled = Scale(Line(0.4m, "cup", "milk"), KitchenTestData.Context(1.5m, KitchenTools.Standard));
        scaled.Explanation.ShouldNotBeNull();
        scaled.Explanation.ShouldStartWith("Exact: ");
        scaled.Explanation.ShouldContain("% off.");
    }

    [Fact]
    public void Metric_ranges_quarts_and_unscaled_lines_are_not_planned()
    {
        var tools = KitchenTools.Standard;
        Scale(Line(0.75m, "cup", "sugar"), KitchenTestData.Context(1.5m, tools, units: DisplayUnits.Metric)).Text.ShouldBe("266 ml sugar");
        Scale(Line(2m, "tbsp", "water", max: 3m), KitchenTestData.Context(2m, tools)).Text.ShouldBe("4–6 Tbsp water");
        Scale(Line(2m, "quart", "stock"), KitchenTestData.Context(2m, tools)).Text.ShouldBe("4 quarts stock");
        Scale(Line(0.375m, "cup", "sugar"), KitchenTestData.Context(1m, tools)).Text.ShouldBe("⅜ cup sugar");
    }

    [Fact]
    public void Without_tools_the_m2_display_is_unchanged()
    {
        Scale(Line(0.75m, "cup", "sugar"), KitchenTestData.Context(1.5m, null)).Text.ShouldBe("1⅛ cups sugar");
    }
}
