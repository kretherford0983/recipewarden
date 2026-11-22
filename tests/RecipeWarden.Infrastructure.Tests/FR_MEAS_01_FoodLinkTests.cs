using Microsoft.EntityFrameworkCore;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>ADR 0013: ingredient lines link to foods on save, and a startup pass links older lines; links are never guessed.</summary>
[Trait("Req", "FR-MEAS-01")]
public sealed class FR_MEAS_01_FoodLinkTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Dictionary<string, int?>> LinksAsync(RecipeTestScope s, int recipeId) =>
        await s.Db.RecipeIngredients.AsNoTracking().Where(i => i.RecipeId == recipeId).ToDictionaryAsync(i => i.Name, i => i.FoodId, Ct);

    private static Task<int> FoodIdAsync(RecipeTestScope s, string name) =>
        s.Db.Foods.Where(f => f.Name == name).Select(f => f.Id).SingleAsync(Ct);

    [Fact]
    public async Task Saving_a_recipe_links_lines_by_name_alias_and_plural()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var saved = await s.AddAsync("Cookies", "2 cups AP flour\n2 large eggs\n1 cup peanut butter\n1/2 cup unobtainium\nsalt to taste");

        var links = await LinksAsync(s, saved.Id);
        links["AP flour"].ShouldBe(await FoodIdAsync(s, "all-purpose flour"));
        links["large eggs"].ShouldBe(await FoodIdAsync(s, "egg"));
        links["peanut butter"].ShouldBe(await FoodIdAsync(s, "peanut butter"));
        links["unobtainium"].ShouldBeNull();
        links["salt"].ShouldBe(await FoodIdAsync(s, "salt"));
    }

    [Fact]
    public async Task The_startup_pass_links_unlinked_lines_only_and_is_repeatable()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var saved = await s.AddAsync("Bread", "3 cups bread flour\n1 tsp salt");
        var honey = await FoodIdAsync(s, "honey");
        await s.Db.RecipeIngredients.Where(i => i.RecipeId == saved.Id && i.Name == "bread flour")
            .ExecuteUpdateAsync(u => u.SetProperty(i => i.FoodId, (int?)null), Ct);
        await s.Db.RecipeIngredients.Where(i => i.RecipeId == saved.Id && i.Name == "salt")
            .ExecuteUpdateAsync(u => u.SetProperty(i => i.FoodId, honey), Ct); // an editor's own mapping

        (await s.Foods.LinkUnmatchedAsync(Ct)).ShouldBe(1);
        (await s.Foods.LinkUnmatchedAsync(Ct)).ShouldBe(0);

        var links = await LinksAsync(s, saved.Id);
        links["bread flour"].ShouldBe(await FoodIdAsync(s, "bread flour"));
        links["salt"].ShouldBe(honey);
    }

    [Fact]
    public async Task Deleting_a_food_keeps_the_lines_and_drops_the_link()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var saved = await s.AddAsync("Toast", "1 tbsp honey");
        (await s.Foods.DeleteFoodAsync(await FoodIdAsync(s, "honey"), Ct)).ShouldBeTrue();

        var line = await s.Db.RecipeIngredients.AsNoTracking().SingleAsync(i => i.RecipeId == saved.Id, Ct);
        line.FoodId.ShouldBeNull();
        line.RawText.ShouldBe("1 tbsp honey");
    }
}
