using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Kitchen;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>FR-TOOL-04/05, BR-31, AC-33, AC-41, AC-42, ADR 0017: suggestions in the page data, and editors' actions.</summary>
[Trait("Req", "FR-TOOL-04")]
public sealed class FR_TOOL_04_SuggestionStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<RecipeView> SaveAsync(RecipeTestScope s, string title, bool precision = true)
    {
        var edit = RecipeTestScope.Edit(title, "2 cups all-purpose flour\n1 cup whole milk") with { IsPrecision = precision };
        var saved = await s.Recipes.CreateAsync(edit, RecipeTestScope.UserId, Ct);
        return (await s.Recipes.GetForReadingAsync(saved.Slug!, true, Ct)).Recipe!;
    }

    private static async Task<RecipeView> ReloadAsync(RecipeTestScope s, RecipeView r) =>
        (await s.Recipes.GetForReadingAsync(r.Slug, true, Ct)).Recipe!;

    private static IEnumerable<string> Texts(Measurement.ScaledRecipe r) => r.Ingredients.SelectMany(g => g.Lines).Select(l => l.Text);

    [Fact]
    [Trait("Req", "AC-41")]
    public async Task A_scale_added_after_a_recipe_is_offered_and_viewing_writes_nothing()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var recipe = await SaveAsync(s, "Banner Bread");
        await s.Kitchen.AddPresetAsync("scale", RecipeTestScope.UserId, Ct);

        var view = await s.Scaler.ScaleAsync(recipe, 1m, DisplayUnits.AsWritten, Ct);

        Texts(view).ShouldBe(["2 cups all-purpose flour", "1 cup whole milk"]);
        var offer = view.Suggestions.ShouldHaveSingleItem();
        offer.Lines.Select(l => l.After).ShouldBe(["250 g all-purpose flour", "244 g whole milk"]);
        (await s.Db.Recipes.AsNoTracking().SingleAsync(r => r.Id == recipe.Id, Ct)).InventoryVersionSeen.ShouldBe(1);
        (await s.Db.RecipeSuggestionStates.CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    [Trait("Req", "AC-41")]
    public async Task Show_me_weighs_for_that_view_only()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var recipe = await SaveAsync(s, "Show Me Bread");
        await s.Kitchen.AddPresetAsync("scale", RecipeTestScope.UserId, Ct);

        var shown = await s.Scaler.ScaleAsync(recipe, 1m, DisplayUnits.AsWritten, showWeights: true, Ct);
        Texts(shown).First().ShouldBe("250 g all-purpose flour");
        shown.ShowingWeights.ShouldBeTrue();
        shown.Suggestions.ShouldBeEmpty();

        Texts(await s.Scaler.ScaleAsync(recipe, 1m, DisplayUnits.AsWritten, Ct)).First().ShouldBe("2 cups all-purpose flour");
    }

    [Fact]
    [Trait("Req", "AC-41")]
    public async Task A_dismissal_is_kept_and_a_different_scale_is_offered_again()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var recipe = await SaveAsync(s, "Dismiss Bread");
        await s.Kitchen.AddPresetAsync("scale", RecipeTestScope.UserId, Ct);
        var key = (await s.Scaler.ScaleAsync(recipe, 1m, DisplayUnits.AsWritten, Ct)).Suggestions.Single().Key;

        (await s.Suggestions.DismissAsync(recipe.Id, key, RecipeTestScope.UserId, Ct)).ShouldBeTrue();
        (await s.Scaler.ScaleAsync(recipe, 1m, DisplayUnits.AsWritten, Ct)).Suggestions.ShouldBeEmpty();

        await s.Kitchen.AddAsync(KitchenRules.Spoon(0.125m, "tsp"), RecipeTestScope.UserId, Ct); // another version, same offer
        (await s.Scaler.ScaleAsync(recipe, 1m, DisplayUnits.AsWritten, Ct)).Suggestions.ShouldBeEmpty();

        await s.Kitchen.AddAsync(new KitchenToolInfo(0, "Second scale", ToolType.Scale, Resolution: 0.1m, ResolutionUnit: "g", SupportedUnits: ["g"]),
            RecipeTestScope.UserId, Ct);
        (await s.Scaler.ScaleAsync(recipe, 1m, DisplayUnits.AsWritten, Ct)).Suggestions.ShouldHaveSingleItem().Key.ShouldNotBe(key);
        (await s.Suggestions.DismissAsync(recipe.Id, "not-a-key", RecipeTestScope.UserId, Ct)).ShouldBeFalse();
    }

    [Fact]
    [Trait("Req", "FR-TOOL-05")]
    public async Task Applying_makes_weights_the_recipe_default()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var recipe = await SaveAsync(s, "Apply Bread");
        await s.Kitchen.AddPresetAsync("scale", RecipeTestScope.UserId, Ct);
        var key = (await s.Scaler.ScaleAsync(recipe, 1m, DisplayUnits.AsWritten, Ct)).Suggestions.Single().Key;

        (await s.Suggestions.ApplyAsync(recipe.Id, key, RecipeTestScope.UserId, Ct)).ShouldBeTrue();
        recipe = await ReloadAsync(s, recipe);

        recipe.InventoryVersionSeen.ShouldBe(await s.Kitchen.CurrentVersionAsync(Ct));
        var view = await s.Scaler.ScaleAsync(recipe, 1m, DisplayUnits.AsWritten, Ct);
        Texts(view).ShouldBe(["250 g all-purpose flour", "244 g whole milk"]);
        view.Ingredients.Single().Lines[0].Secondary.ShouldBe("2 cups");
        view.Suggestions.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Req", "AC-42")]
    public async Task A_new_spoon_changes_no_banner()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var recipe = await SaveAsync(s, "Spoon Bread");
        await s.Kitchen.AddAsync(KitchenRules.Spoon(0.125m, "tsp"), RecipeTestScope.UserId, Ct);
        (await s.Scaler.ScaleAsync(recipe, 1m, DisplayUnits.AsWritten, Ct)).Suggestions.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Req", "AC-33")]
    public async Task With_a_scale_already_in_my_kitchen_a_new_baking_recipe_weighs_flour()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await s.Kitchen.AddPresetAsync("scale", RecipeTestScope.UserId, Ct);
        var edit = RecipeTestScope.Edit("Scale Cake", "2 cups all-purpose flour\n1 cup unobtainium") with { IsPrecision = true };
        var saved = await s.Recipes.CreateAsync(edit, RecipeTestScope.UserId, Ct);
        var recipe = (await s.Recipes.GetForReadingAsync(saved.Slug!, true, Ct)).Recipe!;

        var lines = (await s.Scaler.ScaleAsync(recipe, 1m, DisplayUnits.AsWritten, Ct)).Ingredients.Single().Lines;

        lines[0].Text.ShouldBe("250 g all-purpose flour");
        lines[0].Secondary.ShouldBe("2 cups");
        lines[1].Text.ShouldBe("1 cup unobtainium");
    }

    [Fact]
    [Trait("Req", "AC-32")]
    public async Task Scaled_amounts_use_the_tools_in_my_kitchen()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var saved = await s.Recipes.CreateAsync(RecipeTestScope.Edit("Tool Soup", "3/4 cup sugar"), RecipeTestScope.UserId, Ct);
        var recipe = (await s.Recipes.GetForReadingAsync(saved.Slug!, true, Ct)).Recipe!;
        var view = await s.Scaler.ScaleAsync(recipe, 1.5m, DisplayUnits.AsWritten, Ct);
        var line = view.Ingredients.Single().Lines.Single();
        line.Text.ShouldBe("1 cup + 2 Tbsp sugar");
        line.Explanation.ShouldNotBeNull();
        view.AssumedToolsNote.ShouldBeNull();
    }
}
