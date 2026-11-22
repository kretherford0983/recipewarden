using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Kitchen;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>FR-SCL-04/05/06, BR-111, BR-112, AC-37, AC-39, ADR 0019, ADR 0020: yields, "I made this", and pans.</summary>
[Trait("Req", "FR-SCL-06")]
public sealed class FR_SCL_06_YieldAndPanStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<RecipeView> SaveAsync(RecipeTestScope s, RecipeEdit edit)
    {
        var saved = await s.Recipes.CreateAsync(edit, RecipeTestScope.UserId, Ct);
        saved.Status.ShouldBe(SaveStatus.Saved, string.Join("; ", saved.Errors.Select(e => e.Message)));
        return (await s.Recipes.GetForReadingAsync(saved.Slug!, true, Ct)).Recipe!;
    }

    [Fact]
    [Trait("Req", "AC-39")]
    public async Task Entered_then_measured_yield_and_measured_wins_over_a_resaved_form()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var r = await SaveAsync(s, RecipeTestScope.Edit("Hot Sauce", "1 cup ketchup\n2 cloves garlic") with { YieldText = "10 fl oz" });
        (r.YieldAmount, r.YieldUnitCode, r.YieldSource).ShouldBe((10m, "floz", (YieldSource?)YieldSource.Entered));

        (await s.Recipes.MarkCookedAsync(r.Id, "12 fl oz", RecipeTestScope.UserId, Ct)).Status.ShouldBe(SaveStatus.Saved);
        r = (await s.Recipes.GetForReadingAsync(r.Slug, true, Ct)).Recipe!;
        (r.YieldAmount, r.YieldSource).ShouldBe((12m, (YieldSource?)YieldSource.Measured));
        r.LastCookedAt.ShouldNotBeNull();

        var form = (await s.Recipes.GetForEditAsync(r.Id, Ct))!;
        form.Edit.YieldText.ShouldBe("12 fl oz");
        (await s.Recipes.UpdateAsync(r.Id, form.Edit, form.Version, RecipeTestScope.UserId, false, Ct)).Status.ShouldBe(SaveStatus.Saved);
        (await s.Recipes.GetForReadingAsync(r.Slug, true, Ct)).Recipe!.YieldSource.ShouldBe(YieldSource.Measured);

        (await s.Recipes.MarkCookedAsync(r.Id, "lots", RecipeTestScope.UserId, Ct)).Status.ShouldBe(SaveStatus.Invalid);
        (await s.Recipes.MarkCookedAsync(999_999, null, RecipeTestScope.UserId, Ct)).Status.ShouldBe(SaveStatus.NotFound);
    }

    [Fact]
    [Trait("Req", "AC-39")]
    [Trait("Req", "BR-111")]
    public async Task Container_yield_is_typed_then_stored_then_estimated()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var r = await SaveAsync(s, RecipeTestScope.Edit("Dressing", "1 cup olive oil\n1/4 cup red wine vinegar\n2 cloves garlic"));

        var estimate = await s.Scaler.ContainersAsync(r, null, Ct);
        estimate.Yield!.IsEstimate.ShouldBeTrue();
        estimate.Yield.Excluded.ShouldBe(["garlic"]);
        estimate.Yield.Ml.ShouldBe(1.25m * UnitCatalog.FindByCode("cup")!.ToBase, 0.01m);

        (await s.Scaler.ContainersAsync(r, "14 fl oz", Ct)).Yield!.Source.ShouldBe(YieldSource.Entered);
        (await s.Scaler.ContainersAsync(r, "a lot", Ct)).Error.ShouldNotBeNull();

        await s.Recipes.MarkCookedAsync(r.Id, "11 fl oz", RecipeTestScope.UserId, Ct);
        r = (await s.Recipes.GetForReadingAsync(r.Slug, true, Ct)).Recipe!;
        (await s.Scaler.ContainersAsync(r, null, Ct)).Yield!.Source.ShouldBe(YieldSource.Measured);
    }

    [Fact]
    [Trait("Req", "BR-112")]
    public async Task A_weight_yield_without_a_finished_density_explains_what_is_missing()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var r = await SaveAsync(s, RecipeTestScope.Edit("Granola", "3 cups oats") with { YieldText = "500 g" });
        (await s.Scaler.ContainersAsync(r, null, Ct)).Error.ShouldBe(ContainerRules.WeightYieldNeedsDensity);

        var withDensity = await SaveAsync(s, RecipeTestScope.Edit("Granola 2", "3 cups oats") with { YieldText = "500 g", FinishedDensityGPerMl = 0.4m });
        (await s.Scaler.ContainersAsync(withDensity, null, Ct)).Yield!.Ml.ShouldBe(1250m);
    }

    [Fact]
    [Trait("Req", "AC-37")]
    public async Task Containers_come_from_my_kitchen()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await s.Kitchen.AddPresetAsync("squeeze-bottle", RecipeTestScope.UserId, Ct);
        var r = await SaveAsync(s, RecipeTestScope.Edit("Sauce", "1 cup ketchup") with { YieldText = "12 fl oz" });

        var setup = await s.Scaler.ContainersAsync(r, null, Ct);
        var bottle = setup.Containers.ShouldHaveSingleItem();
        var choice = new ContainerChoice(ContainerRules.Noun(bottle.Name), bottle.CapacityMl!.Value, UnitCatalog.FindByCode(bottle.CapacityUnit)!,
            bottle.DefaultFillPct, 1, bottle.QuantityOwned);
        ContainerRules.Fit(setup.Yield!, choice).ShouldBe(new ContainerFit(1.2m, "fills about 14.4 of 16 fl oz", null, null));
    }

    [Fact]
    [Trait("Req", "FR-MEAS-07")]
    public async Task Pans_are_saved_shown_in_the_editor_and_suggested_when_scaled()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var r = await SaveAsync(s, RecipeTestScope.Edit("Brownies", "1 cup flour") with { PanText = "13 x 9 inch" });
        r.Pan.ShouldBe(new Pan(PanShape.Rectangle, 9m, 13m));
        (await s.Recipes.GetForEditAsync(r.Id, Ct))!.Edit.PanText.ShouldBe("9x13");

        await s.Kitchen.AddPresetAsync("pans", RecipeTestScope.UserId, Ct);
        var doubled = await s.Scaler.ScaleAsync(r, 2m, DisplayUnits.AsWritten, Ct);
        doubled.PanOptions[0].Text.ShouldBe("Half sheet pan (in My Kitchen)");
        doubled.TimeNote.ShouldNotBeNull();
        (await s.Scaler.ScaleAsync(r, 1m, DisplayUnits.AsWritten, Ct)).PanOptions.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_pan_named_in_the_steps_is_used_when_none_is_entered()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var r = await SaveAsync(s, RecipeTestScope.Edit("Sheet Cake", "1 cup flour", "Pour into a greased 9x13-inch pan.\nBake."));
        var view = await s.Scaler.ScaleAsync(r, 2m, DisplayUnits.AsWritten, Ct);
        view.PanFromSteps.ShouldBeTrue();
        view.Pan.ShouldBe(new Pan(PanShape.Rectangle, 9m, 13m));
        view.PanOptions.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Unreadable_yield_or_pan_is_refused()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var outcome = await s.Recipes.CreateAsync(RecipeTestScope.Edit("Bad", "1 cup flour") with { YieldText = "12 jars", PanText = "big" },
            RecipeTestScope.UserId, Ct);
        outcome.Status.ShouldBe(SaveStatus.Invalid);
        outcome.Errors.Select(e => e.Field).ShouldBe([RecipeField.Yield, RecipeField.Pan]);
        (await s.Db.Recipes.CountAsync(r => r.Title == "Bad", Ct)).ShouldBe(0);
    }
}
