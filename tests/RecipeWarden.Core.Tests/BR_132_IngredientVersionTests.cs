using RecipeWarden.Core.Import;

namespace RecipeWarden.Core.Tests;

/// <summary>BR-132, AC-137: two versions of one ingredient group are resolved by the reviewer's choice.</summary>
[Trait("Req", "FR-IMP-04")]
[Trait("Req", "BR-132")]
public sealed class BR_132_IngredientVersionTests
{
    private static readonly ImportedRecipe Corn = new()
    {
        Title = "Skillet Street Corn",
        Ingredients = "Corn:\n2 tbsp olive oil\n32 oz frozen corn\nSauce (Side Dish):\n3 tbsp mayonnaise\n2 tbsp lime juice\nSauce (Taco Topping):\n1/4 cup mayonnaise\n2.5 tbsp lime juice",
        Notes = "Good with tacos.",
        Remarks = ["Two versions of “Sauce” are listed: choose the one to keep.", "Something else."],
    };

    [Fact]
    [Trait("Req", "AC-137")]
    public void The_versions_are_listed_in_order()
    {
        BatchRules.Versions(Corn.Ingredients, "Sauce").ShouldBe(["Side Dish", "Taco Topping"]);
        BatchRules.Versions(Corn.Ingredients, "sauce").ShouldBe(["Side Dish", "Taco Topping"]);
        BatchRules.Versions(Corn.Ingredients, "Glaze").ShouldBeEmpty();
        BatchRules.Versions(null, "Sauce").ShouldBeEmpty();
    }

    [Fact]
    [Trait("Req", "AC-137")]
    public void Keep_only_drops_the_other_version_and_the_warning()
    {
        var (kept, split) = BatchRules.ResolveVersions(Corn, "Sauce", "Taco Topping", VersionChoice.KeepOnly);
        split.ShouldBeEmpty();
        kept.Title.ShouldBe("Skillet Street Corn");
        kept.Ingredients.ShouldBe("Corn:\n2 tbsp olive oil\n32 oz frozen corn\nSauce (Taco Topping):\n1/4 cup mayonnaise\n2.5 tbsp lime juice");
        kept.Notes.ShouldBe("Good with tacos.");
        kept.Remarks.ShouldBe(["Something else."]);
    }

    [Fact]
    [Trait("Req", "AC-137")]
    public void Move_to_notes_keeps_the_other_version_as_a_list_in_the_notes()
    {
        var (kept, split) = BatchRules.ResolveVersions(Corn, "Sauce", "Side Dish", VersionChoice.MoveToNotes);
        split.ShouldBeEmpty();
        kept.Ingredients.ShouldBe("Corn:\n2 tbsp olive oil\n32 oz frozen corn\nSauce (Side Dish):\n3 tbsp mayonnaise\n2 tbsp lime juice");
        kept.Notes.ShouldBe("Good with tacos.\n\nSauce (Taco Topping):\n- 1/4 cup mayonnaise\n- 2.5 tbsp lime juice");

        var (noNotes, _) = BatchRules.ResolveVersions(Corn with { Notes = null }, "Sauce", "Side Dish", VersionChoice.MoveToNotes);
        noNotes.Notes.ShouldBe("Sauce (Taco Topping):\n- 1/4 cup mayonnaise\n- 2.5 tbsp lime juice");
    }

    [Fact]
    [Trait("Req", "AC-137")]
    public void Split_makes_one_recipe_per_version_with_the_shared_groups()
    {
        var (kept, split) = BatchRules.ResolveVersions(Corn, "Sauce", "Side Dish", VersionChoice.Split);
        kept.Title.ShouldBe("Skillet Street Corn (Side Dish)");
        kept.Ingredients.ShouldBe("Corn:\n2 tbsp olive oil\n32 oz frozen corn\nSauce (Side Dish):\n3 tbsp mayonnaise\n2 tbsp lime juice");
        var other = split.ShouldHaveSingleItem();
        other.Title.ShouldBe("Skillet Street Corn (Taco Topping)");
        other.Ingredients.ShouldBe("Corn:\n2 tbsp olive oil\n32 oz frozen corn\nSauce (Taco Topping):\n1/4 cup mayonnaise\n2.5 tbsp lime juice");
        other.Notes.ShouldBe("Good with tacos.");
        other.Remarks.ShouldBe(["Something else."]);
    }

    [Fact]
    [Trait("Req", "AC-137")]
    public void An_unknown_version_changes_nothing()
    {
        var (kept, split) = BatchRules.ResolveVersions(Corn, "Sauce", "Dip", VersionChoice.KeepOnly);
        kept.ShouldBeSameAs(Corn);
        split.ShouldBeEmpty();
        BatchRules.ResolveVersions(Corn, "Glaze", "Side Dish", VersionChoice.Split).Kept.ShouldBeSameAs(Corn);
    }
}
