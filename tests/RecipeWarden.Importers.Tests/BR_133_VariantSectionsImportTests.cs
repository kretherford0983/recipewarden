using RecipeWarden.Core.Import;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Core.Security;
using RecipeWarden.Importers.Import;

namespace RecipeWarden.Importers.Tests;

/// <summary>
/// FR-IMP-04, BR-133, BR-134, AC-138, AC-139: owner testing 2026-11-25 — one "# Title" holding two versions of a
/// recipe under "## " headings (oil and butter wing sauce), one with a larger batch inside it, and yields written
/// under a "### Yield" heading.
/// </summary>
[Trait("Req", "FR-IMP-04")]
public sealed class BR_133_VariantSectionsImportTests
{
    private const string Shared = "A balanced, emulsified buffalo-style sauce with classic heat, tang, and body—ideal for wings, sandwiches, or dipping.";

    private static IReadOnlyList<ImportedRecipe> ReadFixture(string name)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Markdown", name));
        return Read(bytes, name);
    }

    private static IReadOnlyList<ImportedRecipe> Read(byte[] bytes, string name = "recipe.md")
    {
        var result = ImportReader.ReadFile(UploadKind.Markdown, bytes, name);
        result.Error.ShouldBeNull();
        return result.Recipes;
    }

    private static IReadOnlyList<ImportedRecipe> ReadText(string text) => Read(System.Text.Encoding.UTF8.GetBytes(text));

    private static void HasNoMarkup(ImportedRecipe r)
    {
        foreach (var text in new[] { r.Description, r.Ingredients, r.Steps?.Replace("**", string.Empty, StringComparison.Ordinal), r.Notes })
        {
            var t = text ?? string.Empty;
            t.ShouldNotContain("---");
            t.ShouldNotContain("*");
            t.ShouldNotContain("](#");
            t.Split('\n').ShouldAllBe(l => !l.StartsWith('>'));
            t.ShouldNotContain("Top");
        }
    }

    [Fact]
    [Trait("Req", "AC-138")]
    [Trait("Req", "BR-133")]
    [Trait("Req", "BR-134")]
    public void Two_versions_under_one_title_are_two_drafts_with_their_own_yields()
    {
        var recipes = ReadFixture("buffalo-wing-sauce.md");

        recipes.Select(r => r.Title).ShouldBe(
        [
            "Buffalo Wild Wings–Style Medium Sauce (Homemade, Oil Based Sauce)",
            "Buffalo Wild Wings–Style Medium Sauce (Homemade, Butter Based Sauce)",
        ]);

        var oil = recipes[0];
        oil.Description.ShouldNotBeNull();
        oil.Description.ShouldStartWith(Shared);
        oil.Description.ShouldContain("This version is best when you want a smooth, stable, restaurant-style sauce");
        oil.Description.ShouldNotContain("classic, traditional buffalo wing experience");
        oil.Ingredients!.Split('\n').Length.ShouldBe(10);
        oil.Ingredients.Split('\n')[0].ShouldBe("1 cup Frank’s cayenne pepper sauce (e.g., Frank’s RedHot Original)");
        oil.Ingredients.ShouldNotContain("1 1/3 cups");
        StepTextRules.Split(oil.Steps).Count.ShouldBe(6);
        oil.Yield.ShouldBe("about 1 1/2 cups");
        oil.Servings.ShouldBeNull();
        oil.PrepTime.ShouldBe("5 min");
        oil.CookTime.ShouldBe("5 min");
        oil.TotalTime.ShouldBe("10 min");
        oil.Notes.ShouldNotBeNull();
        oil.Notes.ShouldContain("Enough for 3–4 lb of wings, depending on coating preference");
        oil.Notes.ShouldContain("Refrigerated: 2–3 weeks in an airtight container");
        oil.Notes.ShouldContain("Choosing the Right Sauce");
        oil.Notes.ShouldContain("Use the Butter-Based Sauce when:");
        oil.Notes.ShouldContain("Rule of Thumb:");

        // BR-124: the 16 oz batch inside the oil version is folded into its notes, not a third draft.
        var batch = oil.FoldedBatches.ShouldHaveSingleItem();
        batch.Note.ShouldContain("16oz Extra-Thick Batch");
        batch.Note.ShouldContain("1 large egg yolk + 1 tsp egg white");
        batch.Note.ShouldContain("Simmer and thicken");
        oil.Remarks.ShouldHaveSingleItem().ShouldContain("16oz Extra-Thick Batch");

        var butter = recipes[1];
        butter.Description.ShouldNotBeNull();
        butter.Description.ShouldStartWith(Shared);
        butter.Description.ShouldContain("classic, traditional buffalo wing experience");
        butter.Ingredients!.Split('\n').Length.ShouldBe(8);
        butter.Ingredients.Split('\n')[1].ShouldBe("1/4 cup salted butter");
        StepTextRules.Split(butter.Steps).Count.ShouldBe(5);
        butter.Yield.ShouldBe("about 3/4 cup");
        butter.PrepTime.ShouldBe("3 min");
        butter.CookTime.ShouldBe("7 min");
        butter.TotalTime.ShouldBe("10 min");
        butter.Notes.ShouldNotBeNull();
        butter.Notes.ShouldContain("Enough for 1.5–2 lb of wings");
        butter.Notes.ShouldContain("Refrigerated: Up to 1 week in an airtight container");
        butter.Notes.ShouldContain("Choosing the Right Sauce");
        butter.Notes.ShouldNotContain("Refrigerated: 2–3 weeks");
        butter.FoldedBatches.ShouldBeEmpty();
        butter.Remarks.ShouldBeEmpty();

        foreach (var r in recipes)
        {
            HasNoMarkup(r);
        }
    }

    [Fact]
    [Trait("Req", "AC-139")]
    [Trait("Req", "BR-133")]
    public void Parts_of_one_dish_are_not_split()
    {
        // A cake and its frosting each have ingredients and steps, but neither has its own yield, servings or times,
        // and the headings are not version words: one recipe.
        var cake = ReadText("""
            # Birthday Cake

            Serves 12

            ## For the cake

            ### Ingredients
            - 2 cups flour
            - 1 cup sugar

            ### Instructions
            1. Mix.
            2. Bake.

            ## For the frosting

            ### Ingredients
            - 1 cup butter
            - 2 cups powdered sugar

            ### Instructions
            1. Beat until fluffy.
            """).ShouldHaveSingleItem();
        cake.Title.ShouldBe("Birthday Cake");
        cake.Ingredients!.Split('\n').ShouldContain("1 cup butter");
        cake.Ingredients.Split('\n').ShouldContain("2 cups flour");

        // One version heading with ingredients is not two versions.
        var single = ReadText("""
            # Ranch Dressing

            ## Classic Version

            ### Yield
            - About 2 cups

            ### Ingredients
            - 1 cup mayonnaise
            - 1 cup buttermilk

            ### Instructions
            1. Whisk.
            """).ShouldHaveSingleItem();
        single.Title.ShouldBe("Ranch Dressing");
        single.Yield.ShouldBe("about 2 cups");
    }

    [Fact]
    [Trait("Req", "BR-133")]
    public void Versions_split_when_each_has_its_own_facts_and_a_plain_title_gets_brackets()
    {
        var recipes = ReadText("""
            # Ranch Dressing

            ## Thick Version

            - Makes about 2 cups

            ### Ingredients
            - 1 cup mayonnaise
            - 1/2 cup sour cream

            ### Directions
            1. Whisk.

            ## Thin Version

            - Makes about 3 cups

            ### Ingredients
            - 1 cup mayonnaise
            - 1 1/2 cups buttermilk

            ### Directions
            1. Whisk and thin.
            """);
        recipes.Select(r => r.Title).ShouldBe(["Ranch Dressing (Thick Version)", "Ranch Dressing (Thin Version)"]);
        recipes.Select(r => r.Yield).ShouldBe(["about 2 cups", "about 3 cups"]);
        recipes[1].Ingredients.ShouldBe("1 cup mayonnaise\n1 1/2 cups buttermilk");
    }
}
