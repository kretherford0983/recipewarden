using System.Collections.Frozen;

namespace RecipeWarden.Core.Shopping;

/// <summary>A starting package: <see cref="Size"/> in <see cref="UnitCode"/> (null for a plain count), sold as <see cref="Name"/>.</summary>
public sealed record PackageDefault(decimal Size, string? UnitCode, string Name);

/// <summary>
/// ADR 0030, seed shopping-v1: the store section, pantry flag and package size a seeded food starts with. Editors
/// change them on /manage/conversions; the seed never overwrites a value an editor has set.
/// </summary>
public static class ShoppingDefaults
{
    private static readonly FrozenDictionary<string, string> CategorySections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Produce"] = "Produce",
        ["Meat"] = "Meat and Seafood",
        ["Dairy"] = "Dairy and Eggs",
        ["Baking"] = "Baking",
        ["Sweeteners"] = "Baking",
        ["Oils"] = "Pantry",
        ["Nuts"] = "Snacks",
        ["Canned"] = "Canned Goods",
        ["Grains"] = "Pasta and Grains",
        ["Spices"] = "Spices",
        ["Condiments"] = "Condiments",
        ["Other"] = "Beverages",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> NameSections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["frozen peas"] = "Frozen",
        ["corn tortilla"] = "Bakery",
        ["flour tortilla"] = "Bakery",
        ["cooked ham"] = "Deli",
        ["water"] = ShoppingRules.OtherSection,
        ["cooked white rice"] = "Pasta and Grains",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> PantryNames = new[]
    {
        "salt", "black pepper", "water",
        "canola oil", "olive oil", "vegetable oil",
        "all-purpose flour", "granulated sugar", "brown sugar", "powdered sugar", "cornstarch",
        "baking soda", "baking powder", "vanilla extract",
        "apple cider vinegar", "balsamic vinegar", "red wine vinegar", "white vinegar", "soy sauce",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, PackageDefault> Packages = new Dictionary<string, PackageDefault>(StringComparer.OrdinalIgnoreCase)
    {
        ["butter"] = new(0.5m, "cup", "stick"),
        ["black beans"] = new(15m, "oz", "can"),
        ["kidney beans"] = new(15m, "oz", "can"),
        ["pinto beans"] = new(15m, "oz", "can"),
        ["chickpeas"] = new(15m, "oz", "can"),
        ["diced tomatoes"] = new(14.5m, "oz", "can"),
        ["egg"] = new(12m, null, "carton"),
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Section names an editor can pick, in walking order.</summary>
    public static IReadOnlyList<string> Sections => ShoppingRules.SectionOrder;

    public static string SectionFor(string name, string? category) =>
        NameSections.TryGetValue(name, out var byName) ? byName
        : category is not null && CategorySections.TryGetValue(category, out var byCategory) ? byCategory
        : ShoppingRules.OtherSection;

    /// <summary>The starter pantry: basics, oils, flour and sugars, leaveners, vanilla, vinegars, soy sauce and dried spices.</summary>
    public static bool IsPantry(string name, string? category) =>
        PantryNames.Contains(name) || string.Equals(category, "Spices", StringComparison.OrdinalIgnoreCase);

    public static PackageDefault? PackageFor(string name) => Packages.GetValueOrDefault(name);
}
