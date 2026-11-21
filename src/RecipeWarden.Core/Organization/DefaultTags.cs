namespace RecipeWarden.Core.Organization;

/// <summary>One built-in tag.</summary>
public sealed record DefaultTag(string Name, TagType Type);

/// <summary>
/// FR-ORG-01, ADR 0011: tags every install starts with, so common ones are spelled one way. They are ordinary tags:
/// editors can rename or delete them and add their own. Seeded once by the M1_RangesAndDefaultTags migration; an
/// existing tag with the same name is kept and only gets the type. "Homemade Ingredient" marks recipes made to be
/// used in other recipes (onion soup mix, pizza dough, stock).
/// </summary>
public static class DefaultTags
{
    public static IReadOnlyList<DefaultTag> All { get; } =
    [
        new("Breakfast", TagType.Meal),
        new("Brunch", TagType.Meal),
        new("Lunch", TagType.Meal),
        new("Dinner", TagType.Meal),
        new("Snack", TagType.Meal),

        new("Appetizer", TagType.Course),
        new("Entree", TagType.Course),
        new("Side Dish", TagType.Course),
        new("Soup", TagType.Course),
        new("Salad", TagType.Course),
        new("Dessert", TagType.Course),
        new("Bread", TagType.Course),
        new("Sauce", TagType.Course),
        new("Drink", TagType.Course),
        new("Homemade Ingredient", TagType.Course),

        new("Spring", TagType.Season),
        new("Summer", TagType.Season),
        new("Fall", TagType.Season),
        new("Winter", TagType.Season),
        new("Holiday", TagType.Season),

        new("Slow Cooker", TagType.Method),
        new("Pressure Cooker", TagType.Method),
        new("Air Fryer", TagType.Method),
        new("Grill", TagType.Method),
        new("Sheet Pan", TagType.Method),
        new("One Pot", TagType.Method),
        new("No Cook", TagType.Method),

        new("Vegetarian", TagType.Diet),
        new("Vegan", TagType.Diet),
        new("Gluten-Free", TagType.Diet),
        new("Dairy-Free", TagType.Diet),
    ];
}
