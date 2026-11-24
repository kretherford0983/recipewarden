using Microsoft.EntityFrameworkCore;
using RecipeWarden.Tests.Shared;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Shopping;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>FR-SHOP-05, BR-43 to BR-47, AC-51, AC-53, AC-54: the staples list and staples on a list.</summary>
[Trait("Req", "FR-SHOP-05")]
public sealed class FR_SHOP_05_StapleServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Staples_are_foods_or_household_items_and_food_staples_keep_the_pantry_flag_in_step()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        (await s.Staples.AddAsync("eggs", "Fridge", "1 dozen", null, Ct)).ShouldBeNull();
        (await s.Staples.AddAsync("aluminum foil", "Under the sink", null, "Other", Ct)).ShouldBeNull();
        (await s.Staples.AddAsync("Aluminum  Foil", "Pantry", null, null, Ct)).ShouldBe(StapleRules.AlreadyAStaple);
        (await s.Staples.AddAsync("eggs", "Fridge", null, null, Ct)).ShouldBe(StapleRules.AlreadyAStaple);
        (await s.Staples.AddAsync("eggs", "Garage", null, null, Ct)).ShouldBe(StapleRules.BadLocation);

        var egg = await s.Db.Foods.AsNoTracking().SingleAsync(f => f.Name == "egg", Ct);
        egg.IsPantryStaple.ShouldBeTrue();
        var all = await s.Staples.ListAsync(Ct);
        all.Single(x => x.Name == "egg").ShouldSatisfyAllConditions(x => x.Location.ShouldBe("Fridge"), x => x.Restock.ShouldBe("1 dozen"),
            x => x.Section.ShouldBe("Dairy and Eggs"));
        var foil = all.Single(x => x.Name == "aluminum foil");
        foil.FoodId.ShouldBeNull();
        all.ShouldContain(x => x.Name == "salt" && x.Id == 0); // seeded pantry flag, no details yet

        // A flagged food gets its details row on first edit.
        var salt = all.Single(x => x.Name == "salt");
        (await s.Staples.UpdateAsync(0, salt.FoodId, "Spice rack", "1 box", null, Ct)).ShouldBeNull();
        (await s.Staples.ListAsync(Ct)).Single(x => x.Name == "salt").ShouldSatisfyAllConditions(x => x.Id.ShouldBeGreaterThan(0), x => x.Location.ShouldBe("Spice rack"));
        (await s.Staples.UpdateAsync(foil.Id, null, "Pantry", null, "Aisle 9", Ct)).ShouldBe(ShoppingRules.BadSection);
        (await s.Staples.UpdateAsync(0, null, "Pantry", null, null, Ct)).ShouldBe(StapleRules.NameRequired);

        (await s.Staples.RemoveAsync(0, egg.Id, Ct)).ShouldBeTrue();
        (await s.Db.Foods.AsNoTracking().SingleAsync(f => f.Name == "egg", Ct)).IsPantryStaple.ShouldBeFalse();
        (await s.Staples.RemoveAsync(foil.Id, null, Ct)).ShouldBeTrue();
        (await s.Staples.RemoveAsync(999_999, null, Ct)).ShouldBeFalse();
        (await s.Staples.ListAsync(Ct)).ShouldNotContain(x => x.Name == "egg" || x.Name == "aluminum foil");
    }

    [Fact]
    [Trait("Req", "AC-51")]
    [Trait("Req", "BR-44")]
    public async Task A_roux_asks_about_butter_and_flour_and_need_it_moves_butter_to_the_list()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        (await s.Staples.AddAsync("butter", "Fridge", null, null, Ct)).ShouldBeNull();
        (await s.Staples.AddAsync("all-purpose flour", "Pantry", null, null, Ct)).ShouldBeNull();
        var roux = await s.AddAsync("Roux", "4 Tbsp butter\n1/2 cup all-purpose flour");
        var id = (await s.Shopping.CreateAsync("Gumbo night", RecipeTestScope.UserId, Ct)).Id!.Value;
        (await s.Shopping.AddRecipeAsync(id, roux.Slug!, 1m, Ct)).Ok.ShouldBeTrue();

        var view = (await s.Shopping.GetAsync(id, UnitSystem.US, Ct))!.View;
        view.ToBuy.ShouldBeEmpty();
        view.Pantry.Select(i => i.Name).ShouldBe(["all-purpose flour", "butter"]);
        var butterKey = view.Pantry.Single(i => i.Name == "butter").Key;

        (await s.Shopping.AnswerStapleAsync(id, butterKey, StapleAnswer.Need, Ct)).ShouldBeTrue();
        (await s.Shopping.AnswerStapleAsync(id, "f:999999", StapleAnswer.Need, Ct)).ShouldBeFalse();
        (await s.Shopping.AnswerStapleAsync(id, null, StapleAnswer.Need, Ct)).ShouldBeFalse();
        view = (await s.Shopping.GetAsync(id, UnitSystem.US, Ct))!.View;
        view.ToBuy.ShouldHaveSingleItem().Name.ShouldBe("butter");
        view.Pantry.ShouldHaveSingleItem().Name.ShouldBe("all-purpose flour");

        (await s.Shopping.AnswerStapleAsync(id, butterKey, null, Ct)).ShouldBeTrue(); // ask again
        (await s.Shopping.GetAsync(id, UnitSystem.US, Ct))!.View.ToCheck.ShouldBe(2);
        (await s.Shopping.AnswerStapleAsync(id, butterKey, StapleAnswer.Have, Ct)).ShouldBeTrue();
        (await s.Shopping.AnswerStapleAsync(id, butterKey, StapleAnswer.Need, Ct)).ShouldBeTrue();
        (await s.Shopping.GetAsync(id, UnitSystem.US, Ct))!.View.ToBuy.ShouldHaveSingleItem();
    }

    [Fact]
    [Trait("Req", "AC-53")]
    [Trait("Req", "AC-54")]
    [Trait("Req", "BR-45")]
    [Trait("Req", "BR-47")]
    public async Task Restocking_adds_staples_once_merges_with_recipes_and_ticking_records_the_purchase()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero));
        await using var s = await RecipeTestScope.CreateAsync(clock);
        (await s.Staples.AddAsync("eggs", "Fridge", "1 dozen", null, Ct)).ShouldBeNull();
        (await s.Staples.AddAsync("butter", "Fridge", "1 lb", null, Ct)).ShouldBeNull();
        (await s.Staples.AddAsync("aluminum foil", "Under the sink", null, "Other", Ct)).ShouldBeNull();
        var roux = await s.AddAsync("Roux", "4 Tbsp butter\n1/2 cup flour");
        var id = (await s.Shopping.CreateAsync("Restock", RecipeTestScope.UserId, Ct)).Id!.Value;
        await s.Shopping.AddRecipeAsync(id, roux.Slug!, 1m, Ct);

        var staples = await s.Staples.ListAsync(Ct);
        var chosen = staples.Where(x => x.Name is "egg" or "butter" or "aluminum foil").ToList();
        (await s.Shopping.AddRestockAsync(id, chosen, Ct)).Id.ShouldBe(3);
        (await s.Shopping.AddRestockAsync(id, chosen, Ct)).Id.ShouldBe(0); // not twice

        var view = (await s.Shopping.GetAsync(id, UnitSystem.US, Ct))!.View;
        var eggs = view.ToBuy.Single(i => i.Name == "egg");
        eggs.Section.ShouldBe("Dairy and Eggs");
        eggs.AmountText.ShouldBe("12");
        eggs.Recipes.ShouldBe([ShoppingRules.RestockSource]);
        var butter = view.ToBuy.Single(i => i.Name == "butter");
        butter.Recipes.ShouldBe(["Roux", ShoppingRules.RestockSource]);
        view.ToBuy.Single(i => i.Name == "aluminum foil").Section.ShouldBe("Other");
        view.Pantry.ShouldHaveSingleItem().Name.ShouldBe("all-purpose flour"); // a seeded staple, not restocked

        (await s.Shopping.CheckAsync(id, eggs.Key, true, Ct)).Ok.ShouldBeTrue();
        var foilKey = view.ToBuy.Single(i => i.Name == "aluminum foil").Key;
        (await s.Shopping.CheckAsync(id, foilKey, true, Ct)).Ok.ShouldBeTrue();
        var after = await s.Staples.ListAsync(Ct);
        after.Single(x => x.Name == "egg").LastRestockedAt.ShouldBe(clock.GetUtcNow());
        after.Single(x => x.Name == "aluminum foil").LastRestockedAt.ShouldBe(clock.GetUtcNow());
        after.Single(x => x.Name == "butter").LastRestockedAt.ShouldBeNull();
        (await s.Shopping.AddRestockAsync(999_999, chosen, Ct)).Ok.ShouldBeFalse();
    }
}
