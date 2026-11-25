using RecipeWarden.Core.Kitchen;
using RecipeWarden.Core.Measurement;
using static RecipeWarden.Core.Tests.KitchenTestData;

namespace RecipeWarden.Core.Tests;

/// <summary>BR-56, BR-57, AC-135, AC-136: markings on cups and jugs, and what retiring a measure takes away.</summary>
[Trait("Req", "FR-TOOL-01")]
[Trait("Req", "BR-56")]
public sealed class BR_56_GraduatedMeasureTests
{
    private const decimal Cup = 236.5882365m;
    private const decimal Tbsp = 14.78676478125m;

    private static KitchenToolInfo Pyrex(int id = 60, bool active = true) =>
        new KitchenToolInfo(id, "Pyrex 4 cup", ToolType.LiquidMeasure, 4m, "cup", IsActive: active)
        {
            Scales = MeasureScales.Parse("4 cups by 1/2\n1 quart by 1/3\n1000 ml by 100").Scales,
        };

    private static KitchenToolInfo AdjustACup(int id = 61) =>
        new KitchenToolInfo(id, "Adjust-A-Cup", ToolType.MeasuringCup, 1m, "cup") { Scales = MeasureScales.Parse("1/8 to 1 cup by 1/8").Scales };

    [Fact]
    [Trait("Req", "AC-135")]
    public void Markings_are_read_one_set_per_line()
    {
        var (scales, errors) = MeasureScales.Parse("4 cups by 1/2\n1 quart by ⅓\n1000 ml by 100\n⅛ to 1 cup by ⅛\n");
        errors.ShouldBeEmpty();
        scales.ShouldBe(
        [
            new MeasureScale("cup", 0.5m, 4m, 0.5m),
            new MeasureScale("quart", 1m / 3m, 1m, 1m / 3m),
            new MeasureScale("ml", 100m, 1000m, 100m),
            new MeasureScale("cup", 0.125m, 1m, 0.125m),
        ]);
        scales[0].Markings().ShouldBe([0.5m, 1m, 1.5m, 2m, 2.5m, 3m, 3.5m, 4m]);
        scales[1].Markings().Count().ShouldBe(3);
        scales.Select(s => s.Describe()).ShouldBe(["4 cups by ½", "1 quart by ⅓", "1000 ml by 100", "1 cup by ⅛"]); // from ⅛ = the first step
        new MeasureScale("cup", 0.25m, 1m, 0.125m).Describe().ShouldBe("¼ to 1 cup by ⅛");
        MeasureScales.Load(MeasureScales.Store(scales)).ShouldBe(scales);
        MeasureScales.Load(null).ShouldBeNull();
        MeasureScales.Load("cup x 1 1|bogus").ShouldBeNull();
        MeasureScales.Store([]).ShouldBeNull();
    }

    [Theory]
    [InlineData("lots of cups", "Couldn't read")]
    [InlineData("4 widgets by 1", "Couldn't read")]
    [InlineData("4 cups by 0", "Couldn't read")]
    [InlineData("1000 ml by 1", "at most 48 markings")]
    [InlineData("2 to 1 cup by 1/4", "the steps don't fit")]
    [InlineData("1 cup by 1/8\n1 cup by 1/4\n1 cup by 1/3\n1 cup by 1/2\n2 cups by 1", "at most 4 sets")]
    public void Unreadable_markings_say_why(string text, string error)
    {
        MeasureScales.Parse(text).Errors.ShouldContain(e => e.Contains(error, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "AC-135")]
    public void A_marking_is_one_fill_and_a_fixed_measure_wins_a_tie()
    {
        var withJug = StandardWith(Pyrex());
        DisplayPlanner.Plan(1.5m * Cup, StandardWith(), 0.08m)!.FillCount.ShouldBe(2); // 1 cup + ½ cup
        var plan = DisplayPlanner.Plan(1.5m * Cup, withJug, 0.08m)!;
        plan.Text.ShouldBe("1½ cups");
        plan.FillCount.ShouldBe(1);
        plan.Fills.Single().Tool.IsMarking.ShouldBeTrue();

        // 1 cup is both a fixed cup and a jug marking: the fixed cup is used.
        DisplayPlanner.Plan(1m * Cup, withJug, 0.08m)!.Fills.Single().Tool.IsMarking.ShouldBeFalse();

        // A marking with spoons: 2½ cups + 2 Tbsp.
        DisplayPlanner.Plan(2.5m * Cup + 2 * Tbsp, withJug, 0.08m)!.Text.ShouldBe("2½ cups + 2 Tbsp");

        // Small amounts never come from a jug.
        DisplayPlanner.Plan(2 * Tbsp, withJug, 0.08m)!.Fills.ShouldAllBe(f => !f.Tool.IsMarking);

        // A retired jug's markings are not used; quart and ml markings are kept but not used for US plans.
        KitchenTools.From(KitchenRules.StandardSet.Concat([Pyrex(active: false)])).Markings.ShouldBeEmpty();
        withJug.Markings.ShouldAllBe(m => m.CapacityUnit == "cup");
    }

    [Fact]
    public void An_adjustable_cup_measures_five_eighths_in_one_go()
    {
        var plan = DisplayPlanner.Plan(0.625m * Cup, StandardWith(AdjustACup()), 0.02m)!;
        plan.Text.ShouldBe("⅝ cup");
        plan.FillCount.ShouldBe(1);
        DisplayPlanner.Plan(0.625m * Cup, StandardWith(), 0.02m)!.Text.ShouldBe("½ cup + 2 Tbsp");
    }

    [Fact]
    public void Markings_belong_to_cups_spoons_and_jugs_only()
    {
        var (jug, errors) = KitchenRules.FromInput(ToolType.LiquidMeasure, "Pyrex", "4 cups", null, 1, 100m, "4 cups by 1/2");
        errors.ShouldBeEmpty();
        jug.Scales!.Single().Max.ShouldBe(4m);
        KitchenRules.Describe(jug).ShouldBe("4 cups; marked 4 cups by ½");
        KitchenRules.FieldsFor(ToolType.MeasuringCup).Markings.ShouldBeTrue();
        KitchenRules.FieldsFor(ToolType.Container).Markings.ShouldBeFalse();
        KitchenRules.FromInput(ToolType.Container, "Jar", "1 quart", null, 1, 90m, "4 cups by 1/2").Tool.Scales.ShouldBeNull();
        KitchenRules.FromInput(ToolType.MeasuringCup, null, "1 cup", null, 1, 100m, "nonsense").Errors.ShouldNotBeEmpty();
    }

    [Fact]
    [Trait("Req", "AC-136")]
    [Trait("Req", "BR-57")]
    public void Retiring_says_which_sizes_would_be_lost_or_are_still_covered()
    {
        var tools = KitchenRules.StandardSet.Select((t, i) => t with { Id = i + 1 }).Append(Pyrex()).ToList();
        var thirdCup = tools.Single(t => t.CapacityUnit == "cup" && t.CapacityAmount == 1m / 3m);
        var (lost, covered) = MeasureScales.IfRetired(thirdCup, tools);
        lost.ShouldBe(["⅓ cup"]);
        covered.ShouldBeEmpty();

        var oneCup = tools.Single(t => t.CapacityUnit == "cup" && t.CapacityAmount == 1m);
        (lost, covered) = MeasureScales.IfRetired(oneCup, tools);
        lost.ShouldBeEmpty();
        covered.ShouldBe([("1 cup", "Pyrex 4 cup")]);

        (lost, _) = MeasureScales.IfRetired(Pyrex(), tools);
        lost.ShouldBe(["1½ cups", "2 cups", "2½ cups", "3 cups", "3½ cups", "4 cups"]);

        var unnamed = KitchenRules.StandardSet.Select((t, i) => t with { Id = i + 1, Name = string.Empty }).ToList();
        var extraCup = new KitchenToolInfo(99, string.Empty, ToolType.MeasuringCup, 1m, "cup");
        MeasureScales.IfRetired(extraCup, unnamed.Append(extraCup)).Covered.ShouldBe([("1 cup", "your 1 cup measure")]);
        MeasureScales.IfRetired(Scale(), tools).Lost.ShouldBeEmpty(); // a scale gives no spoon or cup sizes
    }
}
