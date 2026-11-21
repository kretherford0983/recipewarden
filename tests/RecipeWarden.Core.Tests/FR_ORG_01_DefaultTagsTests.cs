using RecipeWarden.Core.Organization;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-ORG-01, ADR 0011: the built-in tags are valid, distinct and typed.</summary>
[Trait("Req", "FR-ORG-01")]
public sealed class FR_ORG_01_DefaultTagsTests
{
    [Fact]
    public void Every_default_is_a_valid_tag_name_with_a_distinct_name_and_slug()
    {
        DefaultTags.All.ShouldAllBe(t => NameRules.ValidateTag(t.Name) == null);
        DefaultTags.All.Select(t => NameRules.Normalize(t.Name)).ShouldBeUnique();
        DefaultTags.All.Select(t => SlugRules.FromText(t.Name, "tag")).ShouldBeUnique();
        DefaultTags.All.ShouldAllBe(t => NameRules.Clean(t.Name) == t.Name);
    }

    [Fact]
    public void The_sets_the_owner_chose_are_there()
    {
        Names(TagType.Meal).ShouldBe(["Breakfast", "Brunch", "Lunch", "Dinner", "Snack"]);
        Names(TagType.Course).ShouldBe(["Appetizer", "Entree", "Side Dish", "Soup", "Salad", "Dessert", "Bread", "Sauce", "Drink", "Homemade Ingredient"]);
        Names(TagType.Season).ShouldBe(["Spring", "Summer", "Fall", "Winter", "Holiday"]);
        Names(TagType.Method).ShouldBe(["Slow Cooker", "Pressure Cooker", "Air Fryer", "Grill", "Sheet Pan", "One Pot", "No Cook"]);
        Names(TagType.Diet).ShouldBe(["Vegetarian", "Vegan", "Gluten-Free", "Dairy-Free"]);
        Names(TagType.Cuisine).ShouldBeEmpty(); // cuisines are left to the household
    }

    private static string[] Names(TagType type) => DefaultTags.All.Where(t => t.Type == type).Select(t => t.Name).ToArray();
}
