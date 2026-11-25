using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.AiExchange;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.AiExchange;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Tests.Shared;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>FR-EXP-04, FR-EXP-05, AC-26, AC-27, BR-91 to BR-93, ADR 0033: AI exports and replies saved as variants.</summary>
[Trait("Req", "FR-EXP-05")]
public sealed class FR_EXP_05_AiExchangeServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<SaveOutcome> WebRecipeAsync(RecipeTestScope s)
    {
        var edit = RecipeTestScope.Edit("Stuffed Shells", "4 cloves garlic\n15 oz ricotta", "Mix.\nBake 25 minutes.") with
        {
            Servings = 6m, SourceType = SourceType.Web, SourceName = "Example Kitchen", SourceUrl = "https://example.com/shells",
        };
        var saved = await s.Recipes.CreateAsync(edit, RecipeTestScope.UserId, Ct);
        saved.Status.ShouldBe(SaveStatus.Saved);
        return saved;
    }

    private static string Reply(string slug = "stuffed-shells", string title = "Stuffed Shells") => $"""
        Here you go:

        ```
        ---
        format: recipe-md/1
        title: "{title}"
        slug: "{slug}"
        servings: 6
        source:
          type: web
          name: "Someone Else"
          url: "https://evil.example/"
        ---

        # {title}

        ## Ingredients

        - 2 cloves garlic
        - 15 oz ricotta
        - 1 egg

        ## Steps

        1. Mix.
        2. Bake 25 minutes.

        ## Changes

        - Halved the garlic.
        ```
        """;

    [Fact]
    [Trait("Req", "AC-26")]
    [Trait("Req", "BR-91")]
    public async Task An_export_holds_the_recipe_in_recipe_md_with_its_source_and_the_kitchen_by_name_only()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var saved = await WebRecipeAsync(s);
        var export = await s.Ai.RecipesAsync([saved.Id, 99999], Ct);
        export.Titles.ShouldBe(["Stuffed Shells"]);
        export.Servings.ShouldBe(6m);
        var md = export.Markdown.ShouldHaveSingleItem();
        RecipeMarkdownValidator.Validate(md).ShouldBeEmpty();
        md.ShouldContain("url: \"https://example.com/shells\"");
        md.ShouldNotContain(RecipeTestScope.UserId);

        var kitchen = await s.Ai.KitchenAsync(Ct);
        kitchen.ShouldNotBeEmpty(); // the standard set is seeded
        (await s.Ai.RecipesAsync([], Ct)).Markdown.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Req", "AC-27")]
    [Trait("Req", "BR-92")]
    public async Task A_reply_is_reviewed_against_its_recipe_and_saved_as_a_linked_draft_variant_with_the_original_credit()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));
        await using var s = await RecipeTestScope.CreateAsync(clock);
        var original = await WebRecipeAsync(s);

        var item = (await s.Ai.ReviewAsync(Reply(), null, Ct)).ShouldHaveSingleItem();
        item.CanSave.ShouldBeTrue(string.Join("; ", item.Problems));
        item.Original!.Id.ShouldBe(original.Id);
        item.Ingredients.ShouldContain(new DiffLine(LineChange.Removed, "4 cloves garlic"));
        item.Ingredients.ShouldContain(new DiffLine(LineChange.Added, "1 egg"));
        item.Steps.ShouldAllBe(l => l.Change == LineChange.Same);
        item.Fields.ShouldContain(f => f.Field == RecipeField.Notes);
        item.Fields.ShouldNotContain(f => f.Field == RecipeField.SourceUrl); // BR-92: the reply's source is ignored
        item.Block.Recipe!.Changes.ShouldBe("- Halved the garlic.");

        var saved = await s.Ai.SaveAsync(Reply(), 0, null, AiSaveMode.Variant, publish: false, item.Original.Version, RecipeTestScope.UserId, false, Ct);
        saved.Status.ShouldBe(SaveStatus.Saved);
        saved.RecipeStatus.ShouldBe(RecipeStatus.Draft);
        var variant = (await s.Recipes.GetForReadingAsync(saved.Slug!, true, Ct)).Recipe!;
        variant.Title.ShouldBe("Stuffed Shells (AI variant)");
        variant.SourceUrl.ShouldBe("https://example.com/shells");
        variant.SourceName.ShouldBe("Example Kitchen");
        variant.Notes!.ShouldContain("Changes:\n- Halved the garlic.");
        variant.Notes.ShouldEndWith("Adapted with AI assistance on 2026-11-23.");
        variant.Parent.ShouldBe(new RecipeLink(original.Id, original.Slug!, "Stuffed Shells"));

        var parent = (await s.Recipes.GetForReadingAsync(original.Slug!, true, Ct)).Recipe!;
        parent.Variants.ShouldHaveSingleItem().Slug.ShouldBe(saved.Slug);
        (await s.Recipes.GetForReadingAsync(original.Slug!, false, Ct)).Recipe!.Variants.ShouldBeEmpty(); // drafts hidden from readers
        parent.IngredientsText().ShouldContain("4 cloves garlic"); // the original is unchanged
    }

    [Fact]
    [Trait("Req", "FR-EXP-05")]
    public async Task An_editor_can_replace_the_original_only_at_the_version_reviewed()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var original = await WebRecipeAsync(s);
        var reviewed = (await s.Ai.ReviewAsync(Reply(), original.Slug, Ct)).Single().Original!.Version;

        // Someone edits the recipe after the review.
        var edit = (await s.Recipes.GetForEditAsync(original.Id, Ct))!;
        (await s.Recipes.UpdateAsync(original.Id, edit.Edit with { Notes = "tweak" }, edit.Version, RecipeTestScope.UserId, false, Ct))
            .Status.ShouldBe(SaveStatus.Saved);
        (await s.Ai.SaveAsync(Reply(), 0, original.Slug, AiSaveMode.Replace, true, reviewed, RecipeTestScope.UserId, false, Ct))
            .Status.ShouldBe(SaveStatus.Conflict);

        var current = (await s.Recipes.GetForEditAsync(original.Id, Ct))!.Version;
        var replaced = await s.Ai.SaveAsync(Reply(), 0, original.Slug, AiSaveMode.Replace, true, current, RecipeTestScope.UserId, false, Ct);
        replaced.Status.ShouldBe(SaveStatus.Saved);
        replaced.Id.ShouldBe(original.Id);
        var now = (await s.Recipes.GetForEditAsync(original.Id, Ct))!.Edit;
        now.Title.ShouldBe("Stuffed Shells");
        now.IngredientsText.ShouldBe("2 cloves garlic\n15 oz ricotta\n1 egg");
        (await s.Db.Recipes.CountAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    [Trait("Req", "AC-27")]
    public async Task Unknown_recipes_bad_blocks_and_bad_indexes_save_nothing()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var original = await WebRecipeAsync(s);

        var unknown = (await s.Ai.ReviewAsync(Reply("no-such-recipe"), null, Ct)).Single();
        unknown.CanSave.ShouldBeFalse();
        unknown.Problems.ShouldContain(AiExchangeService.NoRecipe);

        // Opened from the recipe's page, a single block belongs to it whatever its slug.
        (await s.Ai.ReviewAsync(Reply("no-such-recipe"), original.Slug, Ct)).Single().CanSave.ShouldBeTrue();
        (await s.Ai.ReviewAsync(Reply("no-such-recipe"), "../bad", Ct)).Single().CanSave.ShouldBeFalse();

        (await s.Ai.ReviewAsync("No recipe here, sorry.", original.Slug, Ct)).ShouldBeEmpty();
        var noSteps = Reply().Replace("## Steps", "## Method notes", StringComparison.Ordinal);
        (await s.Ai.ReviewAsync(noSteps, original.Slug, Ct)).Single().Problems.ShouldContain("The \"## Steps\" section is missing or empty.");

        (await s.Ai.SaveAsync(noSteps, 0, original.Slug, AiSaveMode.Variant, false, 1, RecipeTestScope.UserId, false, Ct)).Status.ShouldBe(SaveStatus.NotFound);
        (await s.Ai.SaveAsync(Reply(), 5, null, AiSaveMode.Variant, false, 1, RecipeTestScope.UserId, false, Ct)).Status.ShouldBe(SaveStatus.NotFound);
        (await s.Ai.SaveAsync(Reply(), -1, null, AiSaveMode.Variant, false, 1, RecipeTestScope.UserId, false, Ct)).Status.ShouldBe(SaveStatus.NotFound);
        (await s.Db.Recipes.CountAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task A_renamed_recipe_is_found_by_its_old_slug_and_a_new_title_is_kept()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var original = await WebRecipeAsync(s);
        var edit = (await s.Recipes.GetForEditAsync(original.Id, Ct))!;
        await s.Recipes.UpdateAsync(original.Id, edit.Edit with { Title = "Giant Shells" }, edit.Version, RecipeTestScope.UserId, false, Ct);

        var item = (await s.Ai.ReviewAsync(Reply(title: "Lighter Shells"), null, Ct)).Single();
        item.Original!.Slug.ShouldBe("giant-shells");
        var saved = await s.Ai.SaveAsync(Reply(title: "Lighter Shells"), 0, null, AiSaveMode.Variant, true, item.Original.Version, RecipeTestScope.UserId, false, Ct);
        saved.Slug.ShouldBe("lighter-shells");
        saved.RecipeStatus.ShouldBe(RecipeStatus.Published);
    }
}

internal static class RecipeViewTestExtensions
{
    public static string IngredientsText(this RecipeView view) => string.Join('\n', view.Ingredients.SelectMany(g => g.Lines).Select(l => l.RawText));
}
