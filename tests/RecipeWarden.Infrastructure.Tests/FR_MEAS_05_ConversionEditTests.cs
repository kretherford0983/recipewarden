using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Infrastructure.Measurement;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>FR-MEAS-05: editors change densities, aliases, vague values and yields; the engine uses the new values.</summary>
[Trait("Req", "FR-MEAS-05")]
public sealed class FR_MEAS_05_ConversionEditTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<int> FoodIdAsync(RecipeTestScope s, string name) =>
        s.Db.Foods.Where(f => f.Name == name).Select(f => f.Id).SingleAsync(Ct);

    [Fact]
    public async Task Density_and_aliases_can_be_changed_and_bad_values_are_refused()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var flour = await FoodIdAsync(s, "all-purpose flour");

        (await s.Foods.UpdateFoodAsync(flour, 0.51m, "AP flour, plain flour, cake mix flour", RecipeTestScope.UserId, Ct)).Ok.ShouldBeTrue();
        (await s.Foods.UpdateFoodAsync(flour, 53m, null, RecipeTestScope.UserId, Ct)).Error.ShouldBe(FoodRules.ValidateDensity(53m));
        (await s.Foods.UpdateFoodAsync(flour, 0.5m, "butter", RecipeTestScope.UserId, Ct)).Error.ShouldBe(FoodCatalogService.DuplicateName);
        (await s.Foods.UpdateFoodAsync(999_999, 0.5m, null, RecipeTestScope.UserId, Ct)).Error.ShouldBe(FoodCatalogService.NotFound);

        var catalog = await s.Foods.LoadCatalogAsync(Ct);
        catalog.Food(flour)!.DensityGPerMl.ShouldBe(0.51m);
        (await s.Foods.LoadMatcherAsync(Ct)).Match("cake mix flour").ShouldBe(flour);
        (await s.Db.Foods.AsNoTracking().SingleAsync(f => f.Id == flour, Ct)).UpdatedById.ShouldBe(RecipeTestScope.UserId);
    }

    [Fact]
    public async Task A_new_food_links_lines_that_were_unmatched()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var saved = await s.AddAsync("Paella", "1/2 tsp saffron threads");

        (await s.Foods.AddFoodAsync("saffron threads", 0.2m, "saffron", RecipeTestScope.UserId, Ct)).Ok.ShouldBeTrue();
        (await s.Foods.AddFoodAsync("Saffron", null, null, RecipeTestScope.UserId, Ct)).Error.ShouldBe(FoodCatalogService.DuplicateName);
        (await s.Foods.AddFoodAsync(" ", null, null, RecipeTestScope.UserId, Ct)).Ok.ShouldBeFalse();

        var line = await s.Db.RecipeIngredients.AsNoTracking().SingleAsync(i => i.RecipeId == saved.Id, Ct);
        line.FoodId.ShouldBe(await FoodIdAsync(s, "saffron threads"));
    }

    [Fact]
    public async Task Vague_values_are_edited_in_teaspoons_and_used_by_the_engine()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var pinch = (await s.Foods.ListVagueAsync(Ct)).Single(v => v.Code == "pinch");
        pinch.Teaspoons.ShouldBe(0.0625m);
        (await s.Foods.ListVagueAsync(Ct)).ShouldNotContain(v => v.Code == "handful");

        (await s.Foods.UpdateVagueAsync(pinch.Id, 0.125m, Ct)).Ok.ShouldBeTrue();
        (await s.Foods.UpdateVagueAsync(pinch.Id, 9m, Ct)).Ok.ShouldBeFalse();

        var catalog = await s.Foods.LoadCatalogAsync(Ct);
        IngredientScaler.Scale(new ScaleInput(new() { RawText = "a pinch of salt", Quantity = 1m, UnitCode = "pinch", Name = "salt", Confidence = 1m }),
            new ScaleContext(10m, DisplayUnits.AsWritten, false, catalog)).Text.ShouldBe("about 1¼ tsp salt (~10 pinches)");
    }

    [Fact]
    public async Task Yields_are_validated_and_saved()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var row = (await s.Foods.ListYieldsAsync(Ct)).Single(y => y.Food == "chicken breast");

        (await s.Foods.UpdateYieldAsync(row.Id, 1.2m, 1.4m, 0.6m, "household test", RecipeTestScope.UserId, Ct)).Ok.ShouldBeTrue();
        (await s.Foods.UpdateYieldAsync(row.Id, 1.4m, 1.2m, null, "backwards", RecipeTestScope.UserId, Ct)).Ok.ShouldBeFalse();
        (await s.Foods.UpdateYieldAsync(row.Id, 1.2m, 1.4m, null, "", RecipeTestScope.UserId, Ct)).Ok.ShouldBeFalse();

        var saved = (await s.Foods.ListYieldsAsync(Ct)).Single(y => y.Id == row.Id);
        (saved.Ratio, saved.RatioMax, saved.PreparedDensityGPerMl, saved.Basis).ShouldBe((1.2m, 1.4m, (decimal?)0.6m, "household test"));
    }

    [Fact]
    public async Task Foods_can_be_filtered_by_name_or_alias()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        (await s.Foods.ListFoodsAsync("ap flour", Ct)).Select(f => f.Name).ShouldBe(["all-purpose flour"]);
        (await s.Foods.ListFoodsAsync(null, Ct)).Count.ShouldBe(MeasurementSeeder.Load().Foods.Count);
    }

    [Fact]
    [Trait("Req", "FR-UI-02")]
    public async Task Unit_system_and_default_scale_are_saved_to_the_profile()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var userId = await s.AddUserAsync();
        (await s.Preferences.GetDisplayAsync(userId, Ct)).ShouldBe((null, null));

        await s.Preferences.SetDisplayAsync(userId, DisplayUnits.Metric, 2m, Ct);
        (await s.Preferences.GetDisplayAsync(userId, Ct)).ShouldBe((DisplayUnits.Metric, 2m));

        await s.Preferences.SetDisplayAsync(userId, DisplayUnits.US, 500m, Ct);
        (await s.Preferences.GetDisplayAsync(userId, Ct)).ShouldBe((DisplayUnits.US, (decimal?)null));
    }
}
