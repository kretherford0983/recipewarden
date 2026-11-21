using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Security;
using RecipeWarden.Tests.Shared;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>FR-REC-04, BR-05, AC-04: deleted recipes are hidden, listed in trash, restorable for 30 days, then purged.</summary>
[Trait("Req", "FR-REC-04")]
[Trait("Req", "BR-05")]
[Trait("Req", "AC-04")]
public sealed class FR_REC_04_SoftDeleteTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deleted_recipe_is_hidden_from_reading_search_and_listings_but_kept()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var created = await s.AddAsync("Lime Soup", "1 lime", "Cook.", true, "dinner");

        (await s.Recipes.DeleteAsync(created.Id, "u", Ct)).ShouldBeTrue();

        (await s.Recipes.GetForReadingAsync("lime-soup", canSeeDrafts: true, Ct)).Recipe.ShouldBeNull();
        (await s.SearchAsync("lime")).Total.ShouldBe(0);
        (await s.SearchAsync()).Total.ShouldBe(0);
        (await s.Tags.ListAsync(cancellationToken: Ct)).Single().RecipeCount.ShouldBe(0);
        (await s.Db.Recipes.IgnoreQueryFilters().CountAsync(Ct)).ShouldBe(1);
        (await s.Recipes.DeleteAsync(created.Id, "u", Ct)).ShouldBeFalse(); // already in the trash
    }

    [Fact]
    public async Task Trash_lists_deleted_recipes_with_their_purge_date()
    {
        var clock = new ManualClock();
        await using var s = await RecipeTestScope.CreateAsync(clock);
        var created = await s.AddAsync("Soup");
        await s.Recipes.DeleteAsync(created.Id, "u", Ct);

        var item = (await s.Recipes.ListTrashAsync(Ct)).Single();
        item.Title.ShouldBe("Soup");
        item.DeletedAt.ShouldBe(clock.GetUtcNow());
        item.PurgesOn.ShouldBe(clock.GetUtcNow().AddDays(30));
    }

    [Fact]
    public async Task Restore_within_30_days_brings_it_back_everywhere()
    {
        var clock = new ManualClock();
        await using var s = await RecipeTestScope.CreateAsync(clock);
        var created = await s.AddAsync("Lime Soup", "1 lime");
        await s.Recipes.DeleteAsync(created.Id, "u", Ct);
        clock.Advance(TimeSpan.FromDays(29));

        (await s.Recipes.RestoreAsync(created.Id, Ct)).ShouldBeTrue();

        (await s.Recipes.GetForReadingAsync("lime-soup", false, Ct)).Recipe.ShouldNotBeNull();
        (await s.SearchAsync("lime")).Total.ShouldBe(1);
        (await s.Recipes.ListTrashAsync(Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task After_30_days_it_cannot_be_restored_and_is_purged()
    {
        var clock = new ManualClock();
        await using var s = await RecipeTestScope.CreateAsync(clock);
        var keep = await s.AddAsync("Keep");
        var old = await s.AddAsync("Old", "1 egg", "Boil.", true, "breakfast");
        var collection = await s.Collections.CreateAsync("Mornings", null, "u", Ct);
        await s.Collections.AddRecipeAsync(collection.Id, old.Id, Ct);
        await s.Recipes.DeleteAsync(old.Id, "u", Ct);
        clock.Advance(TimeSpan.FromDays(29));
        var recent = await s.AddAsync("Recent");
        await s.Recipes.DeleteAsync(recent.Id, "u", Ct);
        clock.Advance(TimeSpan.FromDays(1));

        (await s.Recipes.RestoreAsync(old.Id, Ct)).ShouldBeFalse();
        (await s.Recipes.PurgeDueAsync(Ct)).ShouldBe(1);

        var left = await s.Db.Recipes.IgnoreQueryFilters().Select(r => r.Title).OrderBy(t => t).ToListAsync(Ct);
        left.ShouldBe(["Keep", "Recent"]);
        (await s.Db.RecipeIngredients.CountAsync(i => i.RecipeId == old.Id, Ct)).ShouldBe(0);
        (await s.Db.RecipeTags.CountAsync(t => t.RecipeId == old.Id, Ct)).ShouldBe(0);
        (await s.Db.CollectionRecipes.CountAsync(Ct)).ShouldBe(0);
        keep.Id.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Only_admin_can_purge_immediately()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var created = await s.AddAsync("Soup");
        await s.Recipes.DeleteAsync(created.Id, "u", Ct);

        await Should.ThrowAsync<UnauthorizedAccessException>(() => s.Recipes.PurgeAsync(created.Id, [AppRoles.Editor], Ct));
        (await s.Db.Recipes.IgnoreQueryFilters().CountAsync(Ct)).ShouldBe(1);

        (await s.Recipes.PurgeAsync(created.Id, [AppRoles.Admin], Ct)).ShouldBeTrue();
        (await s.Db.Recipes.IgnoreQueryFilters().CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Purge_now_only_touches_recipes_in_the_trash()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var live = await s.AddAsync("Soup");

        (await s.Recipes.PurgeAsync(live.Id, [AppRoles.Admin], Ct)).ShouldBeFalse();
        (await s.Db.Recipes.CountAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Restoring_something_not_in_the_trash_does_nothing()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var live = await s.AddAsync("Soup");
        (await s.Recipes.RestoreAsync(live.Id, Ct)).ShouldBeFalse();
        (await s.Recipes.PurgeDueAsync(Ct)).ShouldBe(0);
    }
}
