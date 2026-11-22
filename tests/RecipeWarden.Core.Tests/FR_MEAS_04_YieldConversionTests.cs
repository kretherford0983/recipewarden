using RecipeWarden.Core.Measurement;
using static RecipeWarden.Core.Tests.MeasurementTestData;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-MEAS-04, BR-26, AC-35: prepared amounts estimated as a purchase range with the basis, never exact.</summary>
[Trait("Req", "FR-MEAS-04")]
public sealed class FR_MEAS_04_YieldConversionTests
{
    private const string Basis = "boneless breast, simmered, about 70–88% cooking yield";

    private static ScaledIngredient CookedChicken(decimal qty, string unit, decimal factor = 1m, DisplayUnits units = DisplayUnits.AsWritten) =>
        IngredientScaler.Scale(new ScaleInput(Line(qty, unit, "chicken", prep: "shredded", state: "cooked"), ChickenBreast),
            Context(factor, units));

    [Fact]
    [Trait("Req", "AC-35")]
    public void Four_cups_cooked_shredded_chicken_gives_a_raw_range_with_its_basis()
    {
        var scaled = CookedChicken(4m, "cup");
        scaled.Text.ShouldBe("4 cups chicken, shredded");
        scaled.YieldNote.ShouldBe($"About 1½–1¾ lb raw chicken breast to buy ({Basis}).");
    }

    [Fact]
    [Trait("Req", "AC-35")]
    public void The_purchase_amount_is_kept_for_the_shopping_list()
    {
        var scaled = CookedChicken(4m, "cup");
        scaled.PurchaseGrams!.Value.ShouldBe(636m, 1m);
        scaled.PurchaseGramsMax!.Value.ShouldBe(800m, 1m);
    }

    [Fact]
    [Trait("Req", "BR-26")]
    public void The_estimate_is_always_a_range_never_one_exact_amount()
    {
        foreach (var cups in new[] { 0.5m, 1m, 2m, 3m, 6m })
        {
            var note = CookedChicken(cups, "cup").YieldNote!;
            note.ShouldStartWith("About ");
            note.ShouldContain("–");
            note.ShouldContain(Basis);
        }
    }

    [Fact]
    public void The_estimate_scales_with_the_recipe()
    {
        CookedChicken(4m, "cup", 2m).YieldNote.ShouldBe($"About 2¾–3½ lb raw chicken breast to buy ({Basis}).");
    }

    [Fact]
    public void A_cooked_weight_is_converted_too()
    {
        CookedChicken(1m, "lb").YieldNote.ShouldBe($"About 1¼–1½ lb raw chicken breast to buy ({Basis}).");
    }

    [Fact]
    public void Metric_view_shows_grams()
    {
        CookedChicken(4m, "cup", units: DisplayUnits.Metric).YieldNote.ShouldBe($"About 640–800 g raw chicken breast to buy ({Basis}).");
    }

    [Fact]
    public void Small_amounts_show_ounces()
    {
        CookedChicken(1m, "cup").YieldNote.ShouldBe($"About 6–7 oz raw chicken breast to buy ({Basis}).");
    }

    [Fact]
    public void No_state_or_no_conversion_means_no_note()
    {
        IngredientScaler.Scale(new ScaleInput(Line(1m, "lb", "chicken"), ChickenBreast), Context(1m)).YieldNote.ShouldBeNull();
        IngredientScaler.Scale(new ScaleInput(Line(2m, "cup", "rice", state: "cooked")), Context(1m)).YieldNote.ShouldBeNull();
        IngredientScaler.Scale(new ScaleInput(Line(2m, "cup", "flour", state: "cooked"), Flour), Context(1m)).YieldNote.ShouldBeNull();
    }
}
