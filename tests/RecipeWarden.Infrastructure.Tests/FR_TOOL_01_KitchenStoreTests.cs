using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Kitchen;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>FR-TOOL-01/02/03, BR-30, AC-40, ADR 0021: My Kitchen, its versions and snapshots, and the standard seed.</summary>
[Trait("Req", "FR-TOOL-01")]
public sealed class FR_TOOL_01_KitchenStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static KitchenToolInfo EighthSpoon => KitchenRules.Spoon(0.125m, "tsp");

    [Fact]
    [Trait("Req", "BR-33")]
    public async Task A_new_database_starts_with_the_standard_set_as_version_1()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        (await s.Kitchen.CurrentVersionAsync(Ct)).ShouldBe(1);
        var rows = await s.Kitchen.ListAsync(Ct);
        rows.Select(r => r.Description).ShouldBe(["1 cup", "½ cup", "⅓ cup", "¼ cup", "1 Tbsp", "1 tsp", "½ tsp", "¼ tsp"], ignoreOrder: true);
        (await s.Kitchen.AtVersionAsync(1, Ct)).Measures.Count.ShouldBe(8);
        (await s.Kitchen.CurrentAsync(Ct)).IsAssumedStandard.ShouldBeFalse();
        (await s.Kitchen.AtVersionAsync(0, Ct)).IsAssumedStandard.ShouldBeTrue();
    }

    [Fact]
    [Trait("Req", "BR-30")]
    [Trait("Req", "AC-40")]
    public async Task Add_edit_retire_and_reactivate_each_increment_the_version_and_snapshot()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        (await s.Kitchen.AddAsync(EighthSpoon, RecipeTestScope.UserId, Ct)).Ok.ShouldBeTrue();
        (await s.Kitchen.CurrentVersionAsync(Ct)).ShouldBe(2);
        var id = (await s.Kitchen.ListAsync(Ct)).Single(r => r.Tool.CapacityAmount == 0.125m).Tool.Id;

        (await s.Kitchen.UpdateAsync(id, EighthSpoon with { Name = "Tiny spoon" }, RecipeTestScope.UserId, Ct)).Ok.ShouldBeTrue();
        (await s.Kitchen.SetActiveAsync(id, false, RecipeTestScope.UserId, Ct)).Ok.ShouldBeTrue();
        (await s.Kitchen.CurrentVersionAsync(Ct)).ShouldBe(4);
        (await s.Kitchen.AtVersionAsync(3, Ct)).Measures.ShouldContain(t => t.Name == "Tiny spoon");
        (await s.Kitchen.AtVersionAsync(4, Ct)).Measures.ShouldNotContain(t => t.Id == id);

        var retired = (await s.Kitchen.ListAsync(Ct)).Single(r => r.Tool.Id == id);
        retired.Tool.IsActive.ShouldBeFalse();
        retired.RetiredAt.ShouldNotBeNull();

        (await s.Kitchen.SetActiveAsync(id, true, RecipeTestScope.UserId, Ct)).Ok.ShouldBeTrue();
        (await s.Kitchen.CurrentVersionAsync(Ct)).ShouldBe(5);
        (await s.Kitchen.SetActiveAsync(id, true, RecipeTestScope.UserId, Ct)).Ok.ShouldBeTrue(); // no change, no new version
        (await s.Kitchen.CurrentVersionAsync(Ct)).ShouldBe(5);
    }

    [Fact]
    public async Task Invalid_tools_are_refused_without_a_new_version()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var result = await s.Kitchen.AddAsync(new KitchenToolInfo(0, "", ToolType.Scale), RecipeTestScope.UserId, Ct);
        result.Ok.ShouldBeFalse();
        result.Errors.Count.ShouldBe(3);
        (await s.Kitchen.CurrentVersionAsync(Ct)).ShouldBe(1);
        (await s.Kitchen.UpdateAsync(999_999, EighthSpoon, RecipeTestScope.UserId, Ct)).NotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task Presets_add_what_is_missing_and_bring_back_retired_tools()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        (await s.Kitchen.AddPresetAsync("standard", RecipeTestScope.UserId, Ct)).Ok.ShouldBeTrue();
        (await s.Kitchen.CurrentVersionAsync(Ct)).ShouldBe(1); // nothing was missing

        var tbsp = (await s.Kitchen.ListAsync(Ct)).Single(r => r.Description == "1 Tbsp").Tool.Id;
        await s.Kitchen.SetActiveAsync(tbsp, false, RecipeTestScope.UserId, Ct);
        await s.Kitchen.AddPresetAsync("standard", RecipeTestScope.UserId, Ct);
        (await s.Kitchen.ListAsync(Ct)).Where(r => r.Description == "1 Tbsp").ShouldHaveSingleItem().Tool.IsActive.ShouldBeTrue();

        (await s.Kitchen.AddPresetAsync("scale", RecipeTestScope.UserId, Ct)).Ok.ShouldBeTrue();
        (await s.Kitchen.CurrentAsync(Ct)).Scale.ShouldNotBeNull().Supports("g").ShouldBeTrue();
        (await s.Kitchen.AddPresetAsync("nope", RecipeTestScope.UserId, Ct)).NotFound.ShouldBeTrue();
    }

    [Fact]
    [Trait("Req", "FR-TOOL-03")]
    public async Task Retiring_every_spoon_and_cup_falls_back_to_the_assumed_standard_set()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        foreach (var row in await s.Kitchen.ListAsync(Ct))
        {
            await s.Kitchen.SetActiveAsync(row.Tool.Id, false, RecipeTestScope.UserId, Ct);
        }

        var tools = await s.Kitchen.CurrentAsync(Ct);
        tools.IsAssumedStandard.ShouldBeTrue();
        (await s.Kitchen.ListAsync(Ct)).Count.ShouldBe(8); // kept for history
    }

    [Fact]
    public async Task The_kitchen_seed_runs_once_and_food_forms_are_seeded_once()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var tbsp = (await s.Kitchen.ListAsync(Ct)).Single(r => r.Description == "1 Tbsp").Tool.Id;
        await s.Db.KitchenTools.Where(t => t.Id == tbsp).ExecuteDeleteAsync(Ct);
        await s.Db.Foods.Where(f => f.Name == "whole milk").ExecuteUpdateAsync(u => u.SetProperty(f => f.Form, FoodForm.Other), Ct);

        await s.Initializer.InitializeAsync(Ct);

        (await s.Kitchen.ListAsync(Ct)).ShouldNotContain(r => r.Description == "1 Tbsp");
        var forms = await s.Db.Foods.AsNoTracking().ToDictionaryAsync(f => f.Name, f => f.Form, Ct);
        forms["all-purpose flour"].ShouldBe(FoodForm.Dry);
        forms["butter"].ShouldBe(FoodForm.Fat);
        forms["whole milk"].ShouldBe(FoodForm.Other); // an editor's change (here: back to Other) is not reset
        (await s.Db.SeedRuns.Select(r => r.Name).ToListAsync(Ct)).ShouldBe(["allergens-v1", "food-form-v1", "kitchen-v1", "measurement-v1", "nutrients-foundation-2026-04-fndds-2024-10", "nutrients-sr-legacy-2018-04", "shopping-v1"], ignoreOrder: true); // each seed once; shopping-v1 added in M5 (ADR 0030), nutrients and allergens in M6 (ADR 0034, 0036)
    }
}
