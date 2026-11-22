using RecipeWarden.Core.Kitchen;
using RecipeWarden.Core.Measurement;
using static RecipeWarden.Core.Tests.KitchenTestData;
using static RecipeWarden.Core.Tests.MeasurementTestData;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-TOOL-04/05, BR-31, BR-32, AC-41, AC-42, ADR 0017: suggestions worked out per view, keyed, never written.</summary>
[Trait("Req", "FR-TOOL-04")]
public sealed class FR_TOOL_04_SuggestionEngineTests
{
    private static readonly ScaleInput[] Lines =
    [
        new(Line(2m, "cup", "all-purpose flour"), FlourId),
        new(Line(1m, "cup", "whole milk"), MilkId),
        new(Line(1m, "cup", "mystery")),
    ];

    private static readonly KitchenTools V3 = StandardWith();
    private static readonly KitchenTools V4 = StandardWith(Scale());

    [Fact]
    [Trait("Req", "AC-41")]
    public void A_new_scale_offers_weights_for_the_lines_it_changes()
    {
        var found = SuggestionEngine.Find(Lines, KitchenTestData.Context(1m, null), V3, V4, new HashSet<string>());

        var suggestion = found.ShouldHaveSingleItem();
        suggestion.Key.ShouldBe("weight:scale-50");
        suggestion.Message.ShouldBe("You now have a scale (Digital scale). Show 1 ingredient by weight?");
        suggestion.Lines.ShouldBe([new SuggestionLine(0, "2 cups all-purpose flour", "250 g all-purpose flour")]);
    }

    [Fact]
    [Trait("Req", "AC-41")]
    public void A_baking_recipe_offers_every_line_with_a_density()
    {
        SuggestionEngine.Find(Lines, KitchenTestData.Context(1m, null, precision: true), V3, V4, new HashSet<string>())
            .Single().Lines.Select(l => l.Index).ShouldBe([0, 1]);
    }

    [Fact]
    [Trait("Req", "AC-41")]
    [Trait("Req", "BR-32")]
    public void A_dismissed_or_applied_key_does_not_return_but_a_different_scale_does()
    {
        SuggestionEngine.Find(Lines, KitchenTestData.Context(1m, null), V3, V4, new HashSet<string> { "weight:scale-50" }).ShouldBeEmpty();
        SuggestionEngine.Find(Lines, KitchenTestData.Context(1m, null), V3, StandardWith(Scale(id: 77)), new HashSet<string> { "weight:scale-50" })
            .Single().Key.ShouldBe("weight:scale-77");
    }

    [Fact]
    [Trait("Req", "AC-42")]
    public void A_change_that_does_not_change_the_display_shows_nothing()
    {
        var extraSpoon = StandardWith(KitchenRules.Spoon(0.125m, "tsp") with { Id = 99 });
        SuggestionEngine.Find(Lines, KitchenTestData.Context(1m, null), V3, extraSpoon, new HashSet<string>()).ShouldBeEmpty();
        SuggestionEngine.Find([new ScaleInput(Line(1m, "cup", "mystery"))], KitchenTestData.Context(1m, null), V3, V4, new HashSet<string>()).ShouldBeEmpty();
    }

    [Fact]
    public void A_recipe_accepted_with_a_scale_weighs_without_asking()
    {
        SuggestionEngine.Find(Lines, KitchenTestData.Context(1m, null), V4, V4, new HashSet<string>()).ShouldBeEmpty();
        SuggestionEngine.WeightAccepted(V4, V4).ShouldBeTrue();
        SuggestionEngine.WeightAccepted(V3, V4).ShouldBeFalse();
        SuggestionEngine.WeightAccepted(V4, V3).ShouldBeFalse(); // the scale was retired
    }
}
