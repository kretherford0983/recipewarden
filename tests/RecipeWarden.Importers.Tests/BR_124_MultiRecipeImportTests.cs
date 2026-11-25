using RecipeWarden.Core.Import;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Core.Security;
using RecipeWarden.Importers.Import;

namespace RecipeWarden.Importers.Tests;

/// <summary>
/// FR-IMP-04, BR-124 to BR-127, AC-117 to AC-120: the owner's three test files (2026-11-24) — five variations in one
/// file, one recipe at two batch sizes, and a recipe with sub-headed steps and two sauces.
/// </summary>
[Trait("Req", "FR-IMP-04")]
public sealed class BR_124_MultiRecipeImportTests
{
    private static IReadOnlyList<ImportedRecipe> Read(string name)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Markdown", name));
        var result = ImportReader.ReadFile(UploadKind.Markdown, bytes, name);
        result.Error.ShouldBeNull();
        return result.Recipes;
    }

    private static void HasNoMarkup(ImportedRecipe r)
    {
        foreach (var text in new[] { r.Description, r.Ingredients, r.Steps?.Replace("**", string.Empty, StringComparison.Ordinal), r.Notes })
        {
            (text ?? string.Empty).ShouldNotContain("---");
            (text ?? string.Empty).ShouldNotContain("*");
            (text ?? string.Empty).ShouldNotContain("](#");
        }
    }

    [Fact]
    [Trait("Req", "AC-117")]
    [Trait("Req", "BR-126")]
    public void Five_variations_in_one_file_are_five_recipes_with_headed_steps()
    {
        var recipes = Read("custard-pudding.md");
        recipes.Select(r => r.Title).ShouldBe(
        [
            "Classic Vanilla Custard Pudding", "Rich Chocolate Custard Pudding", "Butterscotch Custard Pudding",
            "Fresh Banana Custard Pudding", "Rich Mocha Espresso Custard Pudding",
        ]);
        recipes.ShouldAllBe(r => r.FoldedBatches.Count == 0);

        var vanilla = recipes[0];
        vanilla.Description.ShouldBe("This silky-smooth, rich vanilla custard pudding is a comforting classic dessert that comes together in under 30 minutes on the stovetop.");
        vanilla.Servings.ShouldBe("6");
        vanilla.PrepTime.ShouldBe("10 min");
        vanilla.CookTime.ShouldBe("15 min");
        vanilla.TotalTime.ShouldBe("25 min");
        vanilla.Ingredients!.Split('\n').Length.ShouldBe(7);
        vanilla.Ingredients.ShouldContain("3 cups (720ml) whole milk");
        var steps = StepTextRules.Split(vanilla.Steps);
        steps.Count.ShouldBe(7);
        steps.ShouldAllBe(s => s.SectionName == null && s.Title != null);
        steps[0].Title.ShouldBe("Combine Dry Ingredients & Milk");
        steps[0].Text.ShouldBe("In a medium saucepan, whisk together the sugar, flour, and salt.");
        steps[6].Title.ShouldBe("Chill");
        vanilla.Nutrition.ShouldNotBeNull();
        vanilla.Nutrition!.Calories.ShouldBe(250m);
        vanilla.Nutrition.SatFatG.ShouldBe(4.5m);
        vanilla.Nutrition.SodiumMg.ShouldBe(145m);
        vanilla.Nutrition.SugarG.ShouldBe(34m);
        recipes.ShouldAllBe(r => r.Remarks.Count == 0);
        foreach (var r in recipes)
        {
            HasNoMarkup(r);
        }
    }

    [Fact]
    [Trait("Req", "AC-118")]
    [Trait("Req", "BR-124")]
    [Trait("Req", "BR-125")]
    public void A_recipe_at_two_batch_sizes_imports_once_with_the_double_batch_in_the_notes()
    {
        var corn = Read("kansas-corn-bake.md").ShouldHaveSingleItem();
        corn.Title.ShouldBe("Kansas Corn Bake");
        corn.Description.ShouldBe("A classic Midwestern casserole-style side dish—rich, savory, and perfect for potlucks or holiday meals.");
        corn.Servings.ShouldBe("6-8");
        corn.PrepTime.ShouldBe("10 min");
        corn.CookTime.ShouldBe("30–40 min");
        corn.TotalTime.ShouldBe("40–50 min");
        corn.Pan.ShouldBe("8x12");
        corn.Ingredients!.Split('\n').Length.ShouldBe(10);
        var steps = StepTextRules.Split(corn.Steps);
        steps.Select(s => s.Title).ShouldBe(["Preheat oven", "Mix ingredients", "Transfer to baking dish", "Bake", "Serve"]);
        HasNoMarkup(corn);

        corn.Notes.ShouldNotBeNull();
        corn.Notes.ShouldContain("Refrigerated: 3–4 days in an airtight container");
        corn.Notes.ShouldContain("Double Batch (from import, 2×):");
        corn.Notes.ShouldContain("Pan: 9x13");
        corn.Notes.ShouldContain("Times: prep 15 min, cook 1 hr 5 min – 1 hr 25 min, total 1 hr 15 min – 1 hr 40 min");
        corn.Notes.ShouldNotContain("Serves 12"); // 6–8 × 2 needs no note
        corn.Notes.ShouldContain("Cover loosely with foil and bake in the middle of the oven for 45-60 minutes");
        corn.Notes.ShouldContain("Using a food processor, process crackers into fine crumbs.");
        corn.Notes.ShouldContain("Center should be set, not soupy");
        corn.Notes.ShouldNotContain("Ingredients that are not 2×:\n- 24 oz"); // the cheese is exactly 2×

        var folded = corn.FoldedBatches.ShouldHaveSingleItem();
        folded.Recipe.Title.ShouldBe("Kansas Corn Bake (Double Batch)");
        corn.Remarks.ShouldContain("Kansas Corn Bake (Double Batch) looked like a 2× batch of this recipe, so it was added to the notes instead of imported.");

        var unfolded = BatchRules.Unfold(corn, 0);
        unfolded.Notes!.ShouldNotContain("Double Batch");
        unfolded.Notes!.ShouldContain("Refrigerated");
        unfolded.Remarks.ShouldBeEmpty();
        unfolded.FoldedBatches.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Req", "AC-119")]
    [Trait("Req", "BR-127")]
    public void Bold_lines_head_their_steps_and_two_sauces_are_flagged()
    {
        var corn = Read("skillet-mexican-street-corn.md").ShouldHaveSingleItem();
        corn.Title.ShouldBe("Skillet Mexican Street Corn");
        corn.Servings.ShouldBe("8");
        corn.PrepTime.ShouldBe("5 min");
        corn.CookTime.ShouldBe("15 min");
        var steps = StepTextRules.Split(corn.Steps);
        steps.Select(s => s.Title).ShouldBe(["Char Corn", "Dressing", "Check Corn", "Coat Corn", "Finish"]);
        steps[4].Text.ShouldBe("Add cotija cheese, red onion, and cilantro and mix until well combined. As side dish: Serve immediately. "
                               + "As taco topping: Spoon the corn mixture directly over your cooked chicken inside the tortillas, then use extra cotija cheese to sprinkle over the top of taco.");
        corn.Ingredients!.ShouldContain("2 (16 ounce) packages frozen corn");
        corn.Ingredients!.ShouldContain("1 pinch (1/16 tsp) ground coriander");
        corn.Ingredients!.ShouldContain("Sauce (Taco Topping):");
        corn.Remarks.ShouldHaveSingleItem().ShouldContain("Two versions of “Sauce”");
        HasNoMarkup(corn);

        // BR-132, AC-137: the owner's file resolves into a side dish and a taco topping, both with the corn group.
        BatchRules.Versions(corn.Ingredients, "Sauce").ShouldBe(["Side Dish", "Taco Topping"]);
        var (side, split) = BatchRules.ResolveVersions(corn, "Sauce", "Side Dish", VersionChoice.Split);
        side.Title.ShouldBe("Skillet Mexican Street Corn (Side Dish)");
        side.Remarks.ShouldBeEmpty();
        side.Ingredients!.ShouldContain("3 tbsp avacado mayonnaise");
        side.Ingredients!.ShouldNotContain("Taco Topping");
        var taco = split.ShouldHaveSingleItem();
        taco.Title.ShouldBe("Skillet Mexican Street Corn (Taco Topping)");
        taco.Ingredients!.ShouldContain("2 (16 ounce) packages frozen corn");
        taco.Ingredients!.ShouldContain("1/4 cup avacado mayonnaise");
        taco.Ingredients!.ShouldNotContain("Side Dish");
    }

    [Fact]
    [Trait("Req", "AC-120")]
    [Trait("Req", "BR-126")]
    public void Different_titles_are_never_folded_however_much_they_share()
    {
        var text = "# Chocolate Custard\n## Ingredients\n- 1 cup sugar\n- 3 cups milk\n- 4 egg yolks\n## Steps\n1. Cook.\n\n"
                   + "# Vanilla Custard\n## Ingredients\n- 2 cups sugar\n- 6 cups milk\n- 8 egg yolks\n## Steps\n1. Cook.\n";
        TextRecipeExtractor.Extract(text).Select(r => r.Title).ShouldBe(["Chocolate Custard", "Vanilla Custard"]);
    }

    [Fact]
    [Trait("Req", "BR-124")]
    public void A_batch_that_is_not_a_consistent_multiple_stays_separate()
    {
        var text = "# Soup (Small Batch)\n## Ingredients\n- 1 cup broth\n- 1 cup rice\n- 1 tsp salt\n## Steps\n1. Cook.\n\n"
                   + "# Soup (Large Batch)\n## Ingredients\n- 4 cups broth\n- 1 cup rice\n- 3 tsp salt\n## Steps\n1. Cook.\n";
        TextRecipeExtractor.Extract(text).Count.ShouldBe(2);
    }
}
