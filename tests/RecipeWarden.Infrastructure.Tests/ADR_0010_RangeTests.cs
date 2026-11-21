using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>ADR 0010: servings and time ranges are stored, read back, shown and filtered on the high end.</summary>
[Trait("Req", "BR-03")]
[Trait("Req", "FR-REC-01")]
[Trait("Req", "FR-SRCH-02")]
public sealed class ADR_0010_RangeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ranges_round_trip_through_the_editor_and_the_reading_view()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var outcome = await s.Recipes.CreateAsync(RecipeTestScope.Edit("Chili") with
        {
            Servings = 6m, ServingsMax = 8m,
            PrepMinutes = 20, PrepMinutesMax = 25,
            CookMinutes = 60, CookMinutesMax = 90,
        }, "u", Ct);

        var edit = (await s.Recipes.GetForEditAsync(outcome.Id, Ct))!.Edit;
        (edit.Servings, edit.ServingsMax).ShouldBe((6m, 8m));
        (edit.PrepMinutes, edit.PrepMinutesMax).ShouldBe((20, 25));
        (edit.CookMinutes, edit.CookMinutesMax).ShouldBe((60, 90));

        var view = (await s.Recipes.GetForReadingAsync("chili", false, Ct)).Recipe!;
        ServingsRules.Format(view.Servings, view.ServingsMax).ShouldBe("6–8");
        (view.TotalMinutes, view.TotalMinutesMax).ShouldBe((80, 115)); // prep + cook, end by end
    }

    [Fact]
    public async Task Invalid_ranges_are_refused()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var outcome = await s.Recipes.CreateAsync(RecipeTestScope.Edit("Bad") with
        {
            Servings = 8m, ServingsMax = 6m,
            CookMinutes = 30, CookMinutesMax = 20,
        }, "u", Ct);

        outcome.Status.ShouldBe(SaveStatus.Invalid);
        outcome.Errors.ShouldBe(
        [
            new PublishProblem(RecipeField.Servings, ServingsRules.HighBelowLow),
            new PublishProblem(RecipeField.CookMinutes, TimeText.HighBelowLow),
        ]);
    }

    [Fact]
    public async Task Estimated_servings_drop_a_stray_high_end()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var outcome = await s.AddAsync("Soup");
        (await s.Db.Recipes.SingleAsync(r => r.Id == outcome.Id, Ct)).ServingsMax.ShouldBeNull();
    }

    [Fact]
    public async Task Ready_in_filter_uses_the_high_end()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await s.Recipes.CreateAsync(RecipeTestScope.Edit("Fits") with { TotalMinutes = 20, TotalMinutesMax = 30 }, "u", Ct);
        await s.Recipes.CreateAsync(RecipeTestScope.Edit("May Not Fit") with { TotalMinutes = 25, TotalMinutesMax = 35 }, "u", Ct);
        await s.Recipes.CreateAsync(RecipeTestScope.Edit("Parts Fit") with { PrepMinutes = 10, CookMinutes = 15, CookMinutesMax = 20 }, "u", Ct);
        await s.Recipes.CreateAsync(RecipeTestScope.Edit("Parts Too Long") with { PrepMinutes = 10, PrepMinutesMax = 15, CookMinutes = 15, CookMinutesMax = 20 }, "u", Ct);

        var items = (await s.SearchAsync(maxMinutes: "30", sort: "title")).Items;

        items.Select(r => r.Title).ShouldBe(["Fits", "Parts Fit"]);
        items.Single(r => r.Title == "Parts Fit").TimeText.ShouldBe("25–30 min");
    }

    [Fact]
    public async Task A_conflict_diff_shows_ranges()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var created = await s.Recipes.CreateAsync(RecipeTestScope.Edit("Stew") with { Servings = 4m }, "u", Ct);
        await s.Recipes.UpdateAsync(created.Id, RecipeTestScope.Edit("Stew") with { Servings = 6m, ServingsMax = 8m }, 1, "u", false, Ct);

        var stale = await s.Recipes.UpdateAsync(created.Id, RecipeTestScope.Edit("Stew") with { Servings = 4m }, 1, "u", false, Ct);
        stale.Differences.Single(d => d.Field == RecipeField.Servings).ShouldBe(new FieldDifference(RecipeField.Servings, "4", "6–8"));
    }
}
