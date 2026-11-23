using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Shopping;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Shopping;
using RecipeWarden.Tests.Shared;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>FR-SHOP-01 to 04, ADR 0028/0029/0030: lists stored per recipe, merged on view, finished and remembered.</summary>
[Trait("Req", "FR-SHOP-01")]
public sealed class FR_SHOP_01_ShoppingServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<int> NewListAsync(RecipeTestScope s, string name = "Saturday")
    {
        var created = await s.Shopping.CreateAsync(name, RecipeTestScope.UserId, Ct);
        created.Ok.ShouldBeTrue();
        return created.Id!.Value;
    }

    [Fact]
    [Trait("Req", "AC-50")]
    public async Task Butter_from_two_recipes_merges_with_sticks_and_both_sources()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var cookies = await s.AddAsync("Cookies", "1 cup butter, softened\n2 cups flour");
        var brownies = await s.AddAsync("Brownies", "4 Tbsp unsalted butter\n1 cup sugar");
        var id = await NewListAsync(s);

        (await s.Shopping.AddRecipeAsync(id, cookies.Slug!, 1m, Ct)).Ok.ShouldBeTrue();
        (await s.Shopping.AddRecipeAsync(id, brownies.Slug!, 1m, Ct)).Ok.ShouldBeTrue();

        var page = (await s.Shopping.GetAsync(id, UnitSystem.US, Ct)).ShouldNotBeNull();
        var butter = page.View.ToBuy.Single(i => i.Name == "butter");
        ShoppingText.Describe(butter, markdown: false).ShouldBe("butter — 1¼ cups (2½ sticks)");
        butter.Recipes.ShouldBe(["Cookies", "Brownies"]);
        butter.Section.ShouldBe("Dairy and Eggs");
        page.View.Pantry.Select(p => p.Name).ShouldBe(["all-purpose flour", "granulated sugar"]);
        page.Recipes.Select(r => r.Title).ShouldBe(["Cookies", "Brownies"]);
    }

    [Fact]
    [Trait("Req", "AC-35")]
    public async Task Cooked_chicken_goes_on_the_list_as_raw_weight_at_the_chosen_scale()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var r = await s.AddAsync("Enchiladas", "4 cups cooked shredded chicken");
        var id = await NewListAsync(s);
        await s.Shopping.AddRecipeAsync(id, r.Slug!, 2m, Ct);

        var item = (await s.Shopping.GetAsync(id, UnitSystem.US, Ct))!.View.ToBuy.ShouldHaveSingleItem();
        item.Name.ShouldBe("chicken breast");
        item.AmountText.ShouldBe("2¾–3½ lb");
        item.Section.ShouldBe("Meat and Seafood");
    }

    [Fact]
    [Trait("Req", "BR-42")]
    public async Task Removing_a_recipe_removes_its_lines_and_later_recipe_edits_do_not_change_the_list()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var r = await s.AddAsync("Soup", "2 carrots\n1 onion");
        var id = await NewListAsync(s);
        var added = await s.Shopping.AddRecipeAsync(id, r.Slug!, 1m, Ct);
        (await s.Shopping.AddItemAsync(id, "2 lemons", Ct)).Ok.ShouldBeTrue();

        var edit = (await s.Recipes.GetForEditAsync(r.Id, Ct))!;
        (await s.Recipes.UpdateAsync(r.Id, edit.Edit with { IngredientsText = "9 potatoes" }, edit.Version, RecipeTestScope.UserId, false, Ct))
            .Status.ShouldBe(Recipes.SaveStatus.Saved);
        (await s.Shopping.GetAsync(id, UnitSystem.US, Ct))!.View.ToBuy.Select(i => i.Name).ShouldBe(["carrot", "lemon", "onion"], ignoreOrder: true);

        (await s.Shopping.RemoveRecipeAsync(id, added.Id!.Value, Ct)).ShouldBeTrue();
        (await s.Shopping.RemoveRecipeAsync(id, added.Id!.Value, Ct)).ShouldBeFalse();
        var page = (await s.Shopping.GetAsync(id, UnitSystem.US, Ct))!;
        page.View.ToBuy.ShouldHaveSingleItem().Name.ShouldBe("lemon");
        page.Manual.ShouldHaveSingleItem().Text.ShouldBe("2 lemons");

        (await s.Shopping.RemoveItemAsync(id, page.Manual[0].Id, Ct)).ShouldBeTrue();
        (await s.Shopping.GetAsync(id, UnitSystem.US, Ct))!.View.ToBuy.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Req", "AC-51")]
    public async Task Ticks_are_stored_on_the_server_and_pantry_items_do_not_hold_the_list_open()
    {
        var clock = new ManualClock();
        await using var s = await RecipeTestScope.CreateAsync(clock);
        var r = await s.AddAsync("Eggs", "6 eggs\n1 tsp salt");
        var id = await NewListAsync(s);
        await s.Shopping.AddRecipeAsync(id, r.Slug!, 1m, Ct);
        var page = (await s.Shopping.GetAsync(id, UnitSystem.US, Ct))!;
        page.View.Pantry.ShouldHaveSingleItem().Name.ShouldBe("salt");
        var egg = page.View.ToBuy.ShouldHaveSingleItem();
        egg.PackageText.ShouldBe("½ carton");

        (await s.Shopping.CheckAsync(id, "f:999999", true, Ct)).Ok.ShouldBeFalse();
        (await s.Shopping.CheckAsync(id, null, true, Ct)).Ok.ShouldBeFalse();
        (await s.Shopping.CheckAsync(id + 100, egg.Key, true, Ct)).Ok.ShouldBeFalse();

        var tick = await s.Shopping.CheckAsync(id, egg.Key, true, Ct);
        tick.ShouldBe(new ShoppingCheckResult(true, true));

        // Another device (a new scope) sees the tick and the undo window.
        await using (var other = s.NewScope())
        {
            var again = (await other.ServiceProvider.GetRequiredService<ShoppingService>().GetAsync(id, UnitSystem.US, Ct))!;
            again.View.ToBuy.Single().IsChecked.ShouldBeTrue();
            again.DeleteAt.ShouldBe(clock.GetUtcNow() + ShoppingService.UndoWindow);
        }

        (await s.Shopping.ListAsync(Ct)).ShouldBeEmpty();

        // Undo: unticking opens it again.
        (await s.Shopping.CheckAsync(id, egg.Key, false, Ct)).ShouldBe(new ShoppingCheckResult(true, false));
        (await s.Shopping.ListAsync(Ct)).ShouldHaveSingleItem().Id.ShouldBe(id);
        (await s.Shopping.GetAsync(id, UnitSystem.US, Ct))!.DeleteAt.ShouldBeNull();
    }

    [Fact]
    [Trait("Req", "ADR-0028")]
    public async Task A_finished_list_is_deleted_after_fifteen_minutes_and_its_name_is_remembered()
    {
        var clock = new ManualClock();
        await using var s = await RecipeTestScope.CreateAsync(clock);
        var id = await NewListAsync(s, "Costco run");
        await s.Shopping.AddItemAsync(id, "1 watermelon", Ct);
        var key = (await s.Shopping.GetAsync(id, UnitSystem.US, Ct))!.View.ToBuy.Single().Key;
        (await s.Shopping.RememberedAsync(Ct)).ShouldBeEmpty(); // still open

        (await s.Shopping.CheckAsync(id, key, true, Ct)).Finished.ShouldBeTrue();
        clock.Advance(TimeSpan.FromMinutes(14));
        (await s.Shopping.GetAsync(id, UnitSystem.US, Ct)).ShouldNotBeNull();
        clock.Advance(TimeSpan.FromMinutes(1));
        (await s.Shopping.GetAsync(id, UnitSystem.US, Ct)).ShouldBeNull();
        (await s.Db.ShoppingLines.CountAsync(Ct)).ShouldBe(0);
        (await s.Shopping.CheckAsync(id, key, false, Ct)).Ok.ShouldBeFalse();

        (await s.Shopping.RememberedAsync(Ct)).ShouldBe(["Costco run"]);
        var again = await NewListAsync(s, "costco run");
        (await s.Shopping.RememberedAsync(Ct)).ShouldBeEmpty();
        (await s.Db.ShoppingListNames.SingleAsync(Ct)).Name.ShouldBe("costco run");
        (await s.Shopping.DeleteAsync(again, Ct)).ShouldBeTrue();
        (await s.Shopping.PurgeFinishedAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    [Trait("Req", "ADR-0028")]
    public async Task Adding_to_a_finished_list_keeps_it()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var id = await NewListAsync(s);
        await s.Shopping.AddItemAsync(id, "milk", Ct);
        var key = (await s.Shopping.GetAsync(id, UnitSystem.US, Ct))!.View.ToBuy.Single().Key;
        await s.Shopping.CheckAsync(id, key, true, Ct);
        await s.Shopping.AddItemAsync(id, "bread", Ct);
        (await s.Shopping.GetAsync(id, UnitSystem.US, Ct))!.DeleteAt.ShouldBeNull();
    }

    [Fact]
    public async Task Names_items_scales_and_limits_are_checked()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        (await s.Shopping.CreateAsync("  ", RecipeTestScope.UserId, Ct)).Error.ShouldBe(ShoppingService.NameRequired);
        (await s.Shopping.CreateAsync(new string('a', 81), RecipeTestScope.UserId, Ct)).Error.ShouldBe(ShoppingService.NameTooLong);
        (await s.Shopping.CreateAsync("Trip\u0007<b>", RecipeTestScope.UserId, Ct)).Ok.ShouldBeTrue();
        (await s.Db.ShoppingLists.SingleAsync(Ct)).Name.ShouldBe("Trip"); // plain text: tags and control characters are removed (BR-13)

        var id = (await s.Db.ShoppingLists.SingleAsync(Ct)).Id;
        (await s.Shopping.AddItemAsync(id, "", Ct)).Error.ShouldBe(ShoppingService.ItemRequired);
        (await s.Shopping.AddItemAsync(id, new string('x', 201), Ct)).Error.ShouldBe(ShoppingService.ItemTooLong);
        (await s.Shopping.AddItemAsync(id + 1, "milk", Ct)).Error.ShouldBe(ShoppingService.NotFound);
        (await s.Shopping.AddRecipeAsync(id, "nope", 1m, Ct)).Error.ShouldBe(ShoppingService.RecipeNotFound);
        (await s.Shopping.AddRecipeAsync(id, "nope", 0m, Ct)).Error.ShouldBe(ShoppingService.BadScale);
        (await s.Shopping.AddRecipeAsync(id + 1, "nope", 1m, Ct)).Error.ShouldBe(ShoppingService.NotFound);
        (await s.Shopping.RemoveItemAsync(id, 12345, Ct)).ShouldBeFalse();
        (await s.Shopping.GetAsync(id + 1, UnitSystem.US, Ct)).ShouldBeNull();

        for (var i = 0; i < ShoppingRules.MaxLinesPerList; i++)
        {
            s.Db.ShoppingLines.Add(new ShoppingLine { ShoppingListId = id, Name = "x", RawText = "x", Position = i });
        }

        await s.Db.SaveChangesAsync(Ct);
        (await s.Shopping.AddItemAsync(id, "milk", Ct)).Error.ShouldBe(ShoppingService.ListFull);
        var r = await s.AddAsync("Toast", "1 slice bread");
        (await s.Shopping.AddRecipeAsync(id, r.Slug!, 1m, Ct)).Error.ShouldBe(ShoppingService.ListFull);
    }

    [Fact]
    public async Task Lists_page_shows_open_lists_with_counts_newest_first_and_metric_amounts_on_request()
    {
        var clock = new ManualClock();
        await using var s = await RecipeTestScope.CreateAsync(clock);
        var first = await NewListAsync(s, "First");
        clock.Advance(TimeSpan.FromMinutes(1));
        var second = await NewListAsync(s, "Second");
        await s.Shopping.AddItemAsync(second, "500 g flour", Ct);
        var lists = await s.Shopping.ListAsync(Ct);
        lists.Select(l => l.Name).ShouldBe(["Second", "First"]);
        lists[0].Items.ShouldBe(1);
        (await s.Shopping.GetAsync(second, UnitSystem.Metric, Ct))!.View.Pantry.Single().AmountText.ShouldBe("500 g");
        (await s.Shopping.GetAsync(first, UnitSystem.US, Ct))!.View.IsFinished.ShouldBeFalse();
    }

    [Fact]
    [Trait("Req", "ADR-0030")]
    public async Task The_shopping_seed_sets_sections_pantry_and_packages_once_and_keeps_editor_values()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var butter = await s.Db.Foods.Include(f => f.Aliases).SingleAsync(f => f.Name == "butter", Ct);
        butter.StoreSection.ShouldBe("Dairy and Eggs");
        butter.PackageName.ShouldBe("stick");
        butter.PackageSize.ShouldBe(0.5m);
        (await s.Db.Foods.SingleAsync(f => f.Name == "salt", Ct)).IsPantryStaple.ShouldBeTrue();
        (await s.Db.Foods.SingleAsync(f => f.Name == "egg", Ct)).PackageUnitId.ShouldBeNull();
        (await s.Db.Foods.SingleAsync(f => f.Name == "frozen peas", Ct)).StoreSection.ShouldBe("Frozen");
        (await s.Db.SeedRuns.CountAsync(r => r.Name == ShoppingSeeder.SeedName, Ct)).ShouldBe(1);

        butter.StoreSection = "Fridge";
        await s.Db.SaveChangesAsync(Ct);
        await s.Initializer.InitializeAsync(Ct);
        (await s.Db.Foods.AsNoTracking().SingleAsync(f => f.Name == "butter", Ct)).StoreSection.ShouldBe("Fridge");
        (await s.Db.SeedRuns.CountAsync(r => r.Name == ShoppingSeeder.SeedName, Ct)).ShouldBe(1);
    }

    [Fact]
    public void A_food_without_a_usable_package_has_no_package()
    {
        var food = new Food { Id = 1, Name = "x", PackageSize = 2m, PackageName = "box" };
        ShoppingService.ToShoppingFood(food, new Unit { Kind = UnitKind.Count, ToBase = 1m }).PackageBase.ShouldBeNull();
        ShoppingService.ToShoppingFood(food, null).PackageBase.ShouldBe(2m);
    }
}
