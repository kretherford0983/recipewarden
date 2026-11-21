using Microsoft.EntityFrameworkCore;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>BR-02, ADR 0008: unique slugs; published title changes keep the old slug as a redirect.</summary>
[Trait("Req", "BR-02")]
public sealed class BR_02_SlugUniquenessTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Same_title_gets_a_numbered_slug()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        (await s.AddAsync("Soup")).Slug.ShouldBe("soup");
        (await s.AddAsync("Soup")).Slug.ShouldBe("soup-2");
        (await s.AddAsync("SOUP!")).Slug.ShouldBe("soup-3");
    }

    [Fact]
    public async Task Database_refuses_a_duplicate_slug()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await s.AddAsync("Soup");
        s.Db.Recipes.Add(new Recipe { Slug = "soup", Title = "Other" });
        await Should.ThrowAsync<DbUpdateException>(() => s.Db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Published_title_change_redirects_from_the_old_slug()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var created = await s.AddAsync("Chicken Soup");

        var updated = await s.Recipes.UpdateAsync(created.Id, RecipeTestScope.Edit("Lemon Chicken Soup"), created.Version, "u", false, Ct);

        updated.Slug.ShouldBe("lemon-chicken-soup");
        var old = await s.Recipes.GetForReadingAsync("chicken-soup", canSeeDrafts: false, Ct);
        old.Recipe.ShouldBeNull();
        old.RedirectToSlug.ShouldBe("lemon-chicken-soup");
    }

    [Fact]
    public async Task Old_slug_is_not_reused_by_a_new_recipe()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var created = await s.AddAsync("Chicken Soup");
        await s.Recipes.UpdateAsync(created.Id, RecipeTestScope.Edit("Lemon Chicken Soup"), created.Version, "u", false, Ct);

        (await s.AddAsync("Chicken Soup")).Slug.ShouldBe("chicken-soup-2");
    }

    [Fact]
    public async Task Changing_the_title_back_makes_the_old_slug_current_again()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var created = await s.AddAsync("Chicken Soup");
        var renamed = await s.Recipes.UpdateAsync(created.Id, RecipeTestScope.Edit("Lemon Chicken Soup"), created.Version, "u", false, Ct);
        var back = await s.Recipes.UpdateAsync(created.Id, RecipeTestScope.Edit("Chicken Soup"), renamed.Version, "u", false, Ct);

        back.Slug.ShouldBe("chicken-soup");
        (await s.Recipes.GetForReadingAsync("lemon-chicken-soup", false, Ct)).RedirectToSlug.ShouldBe("chicken-soup");
        (await s.Db.RecipeSlugAliases.Select(a => a.Slug).ToListAsync(Ct)).ShouldBe(["lemon-chicken-soup"]);
    }

    [Fact]
    public async Task Draft_title_change_moves_the_slug_without_an_alias()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var created = await s.AddAsync("Soup", publish: false);
        var updated = await s.Recipes.UpdateAsync(created.Id, RecipeTestScope.Edit("Stew", publish: false), created.Version, "u", false, Ct);

        updated.Slug.ShouldBe("stew");
        (await s.Db.RecipeSlugAliases.CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Alias_of_a_draft_does_not_redirect_anonymous_readers()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var created = await s.AddAsync("Chicken Soup");
        await s.Recipes.UpdateAsync(created.Id, RecipeTestScope.Edit("Lemon Soup", steps: "", publish: true), created.Version, "u", false, Ct);

        (await s.Recipes.GetForReadingAsync("chicken-soup", canSeeDrafts: false, Ct)).ShouldBe(RecipeLookup.None);
        (await s.Recipes.GetForReadingAsync("chicken-soup", canSeeDrafts: true, Ct)).RedirectToSlug.ShouldBe("lemon-soup");
    }
}
