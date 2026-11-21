using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>FR-REC-01, BR-01, BR-03, BR-04: the recipe aggregate is saved and read back faithfully.</summary>
[Trait("Req", "FR-REC-01")]
public sealed class FR_REC_01_RecipePersistenceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Req", "AC-01")]
    public async Task Complete_recipe_is_published_with_a_slug_and_all_parts()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var edit = new RecipeEdit
        {
            Title = "Chicken Tortilla Soup",
            Description = "Smoky and **bright**.",
            Servings = 6m,
            PrepMinutes = 15,
            CookMinutes = 40,
            IsPrecision = true,
            Notes = "Freezes well.",
            SourceType = SourceType.Web,
            SourceName = "Example Kitchen",
            SourceUrl = "https://example.com/soup",
            SourceAuthor = "Jane Doe",
            IngredientsText = "For the soup:\n1 ½ lb boneless chicken breast\n2 cups diced yellow onion\nTo serve:\n1 pinch cayenne (optional)",
            StepsText = "1. Brown the chicken.\n2. Add the onion.",
            Tags = ["Dinner", "Soup"],
            WantsPublish = true,
        };

        var outcome = await s.Recipes.CreateAsync(edit, RecipeTestScope.UserId, Ct);

        outcome.Status.ShouldBe(SaveStatus.Saved);
        outcome.RecipeStatus.ShouldBe(RecipeStatus.Published);
        outcome.Slug.ShouldBe("chicken-tortilla-soup");
        outcome.Version.ShouldBe(1);

        var view = (await s.Recipes.GetForReadingAsync("chicken-tortilla-soup", canSeeDrafts: false, Ct)).Recipe.ShouldNotBeNull();
        view.Title.ShouldBe("Chicken Tortilla Soup");
        view.Servings.ShouldBe(6m);
        view.ServingsEstimated.ShouldBeFalse();
        view.TotalMinutes.ShouldBe(55); // prep + cook when no total is given
        view.IsPrecision.ShouldBeTrue();
        view.SourceUrl.ShouldBe("https://example.com/soup");
        view.Ingredients.Select(g => g.Name).ShouldBe(["For the soup", "To serve"]);
        view.Ingredients[0].Lines.Select(l => l.Text).ShouldBe(["1½ lb boneless chicken breast", "2 cups yellow onion, diced"]);
        view.Ingredients[1].Lines.Single().IsOptional.ShouldBeTrue();
        view.Steps.Single().Steps.ShouldBe(["Brown the chicken.", "Add the onion."]);
        view.Tags.Select(t => t.Name).ShouldBe(["Dinner", "Soup"]);
    }

    [Fact]
    [Trait("Req", "BR-04")]
    public async Task Raw_ingredient_text_and_parsed_fields_are_both_stored()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var outcome = await s.AddAsync("Bread", ingredients: "1 ½ cups all-purpose flour, sifted");

        var row = await s.Db.RecipeIngredients.Include(i => i.Unit).SingleAsync(i => i.RecipeId == outcome.Id, Ct);
        row.RawText.ShouldBe("1 ½ cups all-purpose flour, sifted");
        row.Quantity.ShouldBe(1.5m);
        row.QuantityText.ShouldBe("1 1/2");
        row.Unit!.Code.ShouldBe("cup");
        row.Name.ShouldBe("all-purpose flour");
        row.Preparation.ShouldBe("sifted");
        row.ParseConfidence.ShouldBeGreaterThanOrEqualTo(0.8m);
    }

    [Fact]
    [Trait("Req", "AC-02")]
    [Trait("Req", "BR-01")]
    public async Task Missing_steps_saves_a_draft_and_reports_why()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var outcome = await s.Recipes.CreateAsync(RecipeTestScope.Edit("Soup", steps: ""), RecipeTestScope.UserId, Ct);

        outcome.Status.ShouldBe(SaveStatus.Saved);
        outcome.RecipeStatus.ShouldBe(RecipeStatus.Draft);
        outcome.PublishProblems.Single().Message.ShouldBe("Add at least one step to publish");
    }

    [Fact]
    [Trait("Req", "BR-03")]
    public async Task Unknown_servings_store_four_estimated()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var outcome = await s.AddAsync("Soup");
        var view = (await s.Recipes.GetForReadingAsync(outcome.Slug!, false, Ct)).Recipe!;
        view.Servings.ShouldBe(4m);
        view.ServingsEstimated.ShouldBeTrue();
    }

    [Fact]
    public async Task Invalid_fields_save_nothing()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var outcome = await s.Recipes.CreateAsync(RecipeTestScope.Edit(new string('t', 201)) with { Servings = -1m }, RecipeTestScope.UserId, Ct);

        outcome.Status.ShouldBe(SaveStatus.Invalid);
        outcome.Errors.Select(e => e.Field).ShouldBe([RecipeField.Title, RecipeField.Servings]);
        (await s.Db.Recipes.CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    [Trait("Req", "FR-REC-02")]
    public async Task Editing_round_trips_through_the_editor_text()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        const string ingredients = "Dough:\n2 cups flour\n1 tsp salt\nFilling:\n1 cup ricotta";
        const string steps = "Dough:\nMix.\nKnead.\nBake:\nBake 20 minutes.";
        var outcome = await s.AddAsync("Calzone", ingredients, steps, true, "Dinner", "dinner", "Italian");

        var edit = (await s.Recipes.GetForEditAsync(outcome.Id, Ct)).ShouldNotBeNull();
        edit.Edit.IngredientsText.ShouldBe(ingredients);
        edit.Edit.StepsText.ShouldBe(steps);
        edit.Edit.Tags.ShouldBe(["Dinner", "Italian"]);
        edit.Version.ShouldBe(1);
    }

    [Fact]
    [Trait("Req", "FR-REC-02")]
    public async Task Reordered_lines_are_saved_in_the_new_order()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var outcome = await s.AddAsync("Soup", "1 onion\n2 carrots", "Chop.\nCook.");

        var updated = await s.Recipes.UpdateAsync(outcome.Id, RecipeTestScope.Edit("Soup", "2 carrots\n1 onion", "Cook.\nChop."),
            outcome.Version, RecipeTestScope.UserId, isAdmin: false, Ct);

        updated.Status.ShouldBe(SaveStatus.Saved);
        updated.Version.ShouldBe(2);
        var view = (await s.Recipes.GetForReadingAsync("soup", false, Ct)).Recipe!;
        view.Ingredients.Single().Lines.Select(l => l.RawText).ShouldBe(["2 carrots", "1 onion"]);
        view.Steps.Single().Steps.ShouldBe(["Cook.", "Chop."]);
        (await s.Db.RecipeIngredients.CountAsync(Ct)).ShouldBe(2); // old rows replaced, not added to
    }

    [Fact]
    [Trait("Req", "BR-11")]
    public async Task Editor_cannot_change_a_web_recipe_to_personal_but_admin_can()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var web = RecipeTestScope.Edit("Soup") with { SourceType = SourceType.Web, SourceName = "Site", SourceUrl = "https://example.com/" };
        var outcome = await s.Recipes.CreateAsync(web, RecipeTestScope.UserId, Ct);

        var byEditor = await s.Recipes.UpdateAsync(outcome.Id, web with { SourceType = SourceType.Personal }, outcome.Version,
            RecipeTestScope.UserId, isAdmin: false, Ct);
        byEditor.Status.ShouldBe(SaveStatus.Invalid);
        byEditor.Errors.Single().Message.ShouldBe(RecipeService.SourceChangeNeedsAdmin);

        var byAdmin = await s.Recipes.UpdateAsync(outcome.Id, web with { SourceType = SourceType.Personal }, outcome.Version,
            RecipeTestScope.UserId, isAdmin: true, Ct);
        byAdmin.Status.ShouldBe(SaveStatus.Saved);
    }

    [Fact]
    [Trait("Req", "FR-REC-06")]
    public async Task Precision_flag_is_stored()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var outcome = await s.Recipes.CreateAsync(RecipeTestScope.Edit("Macarons") with { IsPrecision = true }, RecipeTestScope.UserId, Ct);
        (await s.Recipes.GetForEditAsync(outcome.Id, Ct))!.Edit.IsPrecision.ShouldBeTrue();
    }

    [Fact]
    public async Task Missing_recipes_are_reported()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        (await s.Recipes.GetForEditAsync(999, Ct)).ShouldBeNull();
        (await s.Recipes.UpdateAsync(999, RecipeTestScope.Edit("x"), 1, RecipeTestScope.UserId, false, Ct)).Status.ShouldBe(SaveStatus.NotFound);
        (await s.Recipes.GetForReadingAsync("nope", true, Ct)).ShouldBe(RecipeLookup.None);
        (await s.Recipes.GetForReadingAsync("../etc", true, Ct)).ShouldBe(RecipeLookup.None);
    }

    [Fact]
    [Trait("Req", "FR-ORG-02")]
    public async Task Reading_view_lists_the_collections_holding_the_recipe()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var recipe = await s.AddAsync("Soup");
        var weeknight = await s.Collections.CreateAsync("Weeknight", null, RecipeTestScope.UserId, Ct);
        await s.Collections.AddRecipeAsync(weeknight.Id, recipe.Id, Ct);

        var view = (await s.Recipes.GetForReadingAsync("soup", false, Ct)).Recipe!;
        view.Collections.Single().Name.ShouldBe("Weeknight");
    }
}
