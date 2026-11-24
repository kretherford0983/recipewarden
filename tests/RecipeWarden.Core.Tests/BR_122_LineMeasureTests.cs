using RecipeWarden.Core.Kitchen;
using RecipeWarden.Core.Measurement;
using static RecipeWarden.Core.Tests.KitchenTestData;
using static RecipeWarden.Core.Tests.MeasurementTestData;

namespace RecipeWarden.Core.Tests;

/// <summary>BR-122, BR-123, AC-115, AC-116: each ingredient line can be weighed or kept as written.</summary>
[Trait("Req", "FR-TOOL-05")]
[Trait("Req", "BR-122")]
public sealed class BR_122_LineMeasureTests
{
    private static readonly KitchenTools WithScale = StandardWith(Scale());

    private static ScaledIngredient Weigh(Recipes.ParsedIngredient line, LineMeasure measure, ScaleContext context, int? food) =>
        IngredientScaler.Scale(new ScaleInput(line, food, Measure: measure), context);

    [Fact]
    public void By_weight_weighs_a_line_even_in_an_ordinary_recipe_without_an_accepted_scale()
    {
        var ctx = KitchenTestData.Context(1m, WithScale, preferWeight: false);
        var milk = Weigh(Line(1m, "cup", "whole milk"), LineMeasure.Weight, ctx, MilkId);
        milk.Text.ShouldBe("244 g whole milk");
        milk.Secondary.ShouldBe("1 cup");
        milk.ByWeight.ShouldBeTrue();
    }

    [Fact]
    public void As_written_is_never_weighed_even_in_a_baking_recipe()
    {
        var ctx = KitchenTestData.Context(1m, WithScale, precision: true, preferWeight: true);
        Weigh(Line(2m, "cup", "all-purpose flour"), LineMeasure.AsWritten, ctx, FlourId).Text.ShouldBe("2 cups all-purpose flour");
        Weigh(Line(2m, "cup", "all-purpose flour"), LineMeasure.Auto, ctx, FlourId).Text.ShouldBe("250 g all-purpose flour");
    }

    [Fact]
    public void By_weight_needs_a_scale_and_a_density()
    {
        Weigh(Line(1m, "cup", "whole milk"), LineMeasure.Weight, KitchenTestData.Context(1m, StandardWith()), MilkId)
            .ByWeight.ShouldBeFalse();
        Weigh(Line(1m, "cup", "mystery flour"), LineMeasure.Weight, KitchenTestData.Context(1m, WithScale), null)
            .ByWeight.ShouldBeFalse();
    }

    [Fact]
    [Trait("Req", "BR-123")]
    [Trait("Req", "AC-115")]
    public void A_small_weight_on_a_1_g_scale_names_the_spoon_amount()
    {
        var ctx = KitchenTestData.Context(1m, WithScale);
        var pepper = Weigh(Line(0.5m, "tsp", "granulated sugar"), LineMeasure.Weight, ctx, SugarId);
        pepper.Text.ShouldBe("2 g granulated sugar");
        pepper.Hint.ShouldBe("Small amount for this scale: weigh onto a zeroed plate, or use ½ tsp.");

        Weigh(Line(1m, "cup", "granulated sugar"), LineMeasure.Weight, ctx, SugarId).Hint.ShouldBeNull();
    }

    [Theory]
    [Trait("Req", "BR-123")]
    [InlineData(4.9, 1.0, true)]
    [InlineData(5.0, 1.0, false)]
    [InlineData(10, 2.835, true)]
    [InlineData(1, 0, false)]
    public void The_small_amount_threshold_is_five_steps(double grams, double step, bool hinted)
    {
        (LineMeasureRules.SmallAmountHint((decimal)grams, (decimal)step, null) is not null).ShouldBe(hinted);
        LineMeasureRules.SmallAmountHint(1m, 1m, null).ShouldBe("Small amount for this scale: weigh onto a zeroed plate.");
    }

    [Fact]
    public void Choices_follow_unchanged_lines_and_the_form_wins()
    {
        var previous = new Dictionary<string, LineMeasure>(StringComparer.Ordinal)
        {
            [LineMeasureRules.Key("1/2 tsp ground black pepper")] = LineMeasure.Weight,
            [LineMeasureRules.Key("1 tsp salt")] = LineMeasure.AsWritten,
        };
        LineMeasureRules.Carry("1/2  tsp Ground black pepper ", previous, null).ShouldBe(LineMeasure.Weight);
        LineMeasureRules.Carry("1 tsp kosher salt", previous, null).ShouldBe(LineMeasure.Auto);
        var chosen = new Dictionary<string, LineMeasure>(StringComparer.Ordinal) { [LineMeasureRules.Key("1 tsp salt")] = LineMeasure.Auto };
        LineMeasureRules.Carry("1 tsp salt", previous, chosen).ShouldBe(LineMeasure.Auto);
    }

    [Fact]
    [Trait("Req", "AC-115")]
    public void Applying_a_suggestion_weighs_picked_lines_and_keeps_the_rest_as_written()
    {
        LineMeasureRules.Apply([0, 1, 2, 3], [1, 9]).ShouldBe(new Dictionary<int, LineMeasure>
        {
            [0] = LineMeasure.AsWritten, [1] = LineMeasure.Weight, [2] = LineMeasure.AsWritten, [3] = LineMeasure.AsWritten,
        });
        LineMeasureRules.Apply([0, 1], null).Values.ShouldAllBe(m => m == LineMeasure.Weight);
    }

    [Fact]
    [Trait("Req", "AC-116")]
    public void Show_me_reads_ticked_lines()
    {
        LineMeasureRules.ParseShow("show").ShouldBeNull();
        LineMeasureRules.ParseShow("1,3, x,-2").ShouldBe(new HashSet<int> { 1, 3 }, ignoreOrder: true);
        LineMeasureRules.ParseShow(null, [2, -1]).ShouldBe(new HashSet<int> { 2 }, ignoreOrder: true);
        LineMeasureRules.ParseShow(string.Empty).ShouldBeEmpty();
    }

    [Fact]
    public void Lines_with_their_own_choice_are_not_offered_again()
    {
        var lines = new List<ScaleInput>
        {
            new(Line(1m, "cup", "granulated sugar"), SugarId, Measure: LineMeasure.AsWritten),
            new(Line(2m, "cup", "all-purpose flour"), FlourId),
            new(Line(1m, "cup", "granulated sugar"), SugarId, Measure: LineMeasure.Weight),
        };
        var offers = SuggestionEngine.Find(lines, KitchenTestData.Context(1m, WithScale), StandardWith(), WithScale, new HashSet<string>());
        offers.Single().Lines.Select(l => l.Index).ShouldBe([1]);
    }
}
