using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>FR-ORG-02, AC-20: collections can be created, reordered and filled; readers see published recipes only.</summary>
[Trait("Req", "FR-ORG-02")]
[Trait("Req", "AC-20")]
public sealed class FR_ORG_02_CollectionStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Collections_are_created_in_order_and_can_be_reordered()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var a = await s.Collections.CreateAsync("Weeknight", "Fast dinners", "u", Ct);
        var b = await s.Collections.CreateAsync("Holiday baking", null, "u", Ct);
        var c = await s.Collections.CreateAsync("Soups", null, "u", Ct);

        (await s.Collections.ListAsync(Ct)).Select(x => x.Name).ShouldBe(["Weeknight", "Holiday baking", "Soups"]);

        (await s.Collections.MoveAsync(c.Id, -1, Ct)).ShouldBeTrue();
        (await s.Collections.MoveAsync(a.Id, 1, Ct)).ShouldBeTrue();
        (await s.Collections.ListAsync(Ct)).Select(x => x.Name).ShouldBe(["Soups", "Weeknight", "Holiday baking"]);

        (await s.Collections.MoveAsync(c.Id, -1, Ct)).ShouldBeFalse(); // already first
        (await s.Collections.MoveAsync(999, 1, Ct)).ShouldBeFalse();
        b.Slug.ShouldBe("holiday-baking");
    }

    [Fact]
    public async Task Recipes_are_added_once_in_order_and_can_be_moved_and_removed()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var soup = await s.AddAsync("Soup");
        var stew = await s.AddAsync("Stew");
        var chili = await s.AddAsync("Chili");
        var c = await s.Collections.CreateAsync("Winter", null, "u", Ct);

        foreach (var r in new[] { soup, stew, chili, soup })
        {
            (await s.Collections.AddRecipeAsync(c.Id, r.Id, Ct)).ShouldBeTrue();
        }

        (await s.Collections.GetBySlugAsync("winter", false, Ct))!.Recipes.Select(r => r.Title).ShouldBe(["Soup", "Stew", "Chili"]);

        await s.Collections.MoveRecipeAsync(c.Id, chili.Id, -1, Ct);
        await s.Collections.RemoveRecipeAsync(c.Id, soup.Id, Ct);
        (await s.Collections.GetBySlugAsync("winter", false, Ct))!.Recipes.Select(r => r.Title).ShouldBe(["Chili", "Stew"]);

        (await s.Collections.AddRecipeAsync(c.Id, 999, Ct)).ShouldBeFalse();
        (await s.Collections.AddRecipeAsync(999, soup.Id, Ct)).ShouldBeFalse();
        (await s.Collections.MoveRecipeAsync(c.Id, 999, 1, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Readers_see_only_published_recipes_and_counts()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var published = await s.AddAsync("Soup");
        var draft = await s.AddAsync("Secret", publish: false);
        var c = await s.Collections.CreateAsync("Mixed", null, "u", Ct);
        await s.Collections.AddRecipeAsync(c.Id, published.Id, Ct);
        await s.Collections.AddRecipeAsync(c.Id, draft.Id, Ct);

        (await s.Collections.GetBySlugAsync("mixed", includeDrafts: false, Ct))!.Recipes.Select(r => r.Title).ShouldBe(["Soup"]);
        (await s.Collections.GetBySlugAsync("mixed", includeDrafts: true, Ct))!.Recipes.Count.ShouldBe(2);
        (await s.Collections.ListAsync(Ct)).Single().RecipeCount.ShouldBe(1);
    }

    [Fact]
    public async Task Names_are_unique_case_insensitively_and_validated()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await s.Collections.CreateAsync("Weeknight", null, "u", Ct);

        (await s.Collections.CreateAsync("WEEKNIGHT", null, "u", Ct)).Errors.Single().Message.ShouldBe(CollectionService.Duplicate);
        (await s.Collections.CreateAsync(" ", null, "u", Ct)).Status.ShouldBe(SaveStatus.Invalid);
        (await s.Collections.CreateAsync("Long", new string('d', 4001), "u", Ct)).Status.ShouldBe(SaveStatus.Invalid);
    }

    [Fact]
    public async Task Update_checks_the_version_and_renames_the_slug()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var c = await s.Collections.CreateAsync("Weeknight", null, "u", Ct);

        var updated = await s.Collections.UpdateAsync(c.Id, "School nights", "Fast", c.Version, "u", Ct);
        updated.Status.ShouldBe(SaveStatus.Saved);
        updated.Slug.ShouldBe("school-nights");

        var stale = await s.Collections.UpdateAsync(c.Id, "Other", null, c.Version, "u", Ct);
        stale.Status.ShouldBe(SaveStatus.Conflict);
        stale.Errors.Single().Message.ShouldBe(CollectionService.Changed);

        (await s.Collections.UpdateAsync(999, "x", null, 1, "u", Ct)).Status.ShouldBe(SaveStatus.NotFound);
        (await s.Collections.GetBySlugAsync("weeknight", false, Ct)).ShouldBeNull();
        (await s.Collections.GetBySlugAsync("../x", false, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Deleting_a_collection_keeps_its_recipes()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var soup = await s.AddAsync("Soup");
        var c = await s.Collections.CreateAsync("Winter", null, "u", Ct);
        await s.Collections.AddRecipeAsync(c.Id, soup.Id, Ct);

        (await s.Collections.DeleteAsync(c.Id, Ct)).ShouldBeTrue();
        (await s.Collections.ListAsync(Ct)).ShouldBeEmpty();
        (await s.Recipes.GetForReadingAsync("soup", false, Ct)).Recipe.ShouldNotBeNull();
    }
}
