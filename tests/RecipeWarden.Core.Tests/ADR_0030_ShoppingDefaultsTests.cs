using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Core.Shopping;

namespace RecipeWarden.Core.Tests;

/// <summary>ADR 0030 (seed shopping-v1) and ADR 0029 (stored amounts): defaults and line amounts.</summary>
[Trait("Req", "ADR-0030")]
public sealed class ADR_0030_ShoppingDefaultsTests
{
    [Theory]
    [InlineData("butter", "Dairy", "Dairy and Eggs")]
    [InlineData("frozen peas", "Produce", "Frozen")]
    [InlineData("flour tortilla", "Grains", "Bakery")]
    [InlineData("salmon", "Meat", "Meat and Seafood")]
    [InlineData("mystery", null, "Other")]
    [InlineData("mystery", "Unknown", "Other")]
    public void Sections_come_from_the_name_then_the_category(string name, string? category, string section) =>
        ShoppingDefaults.SectionFor(name, category).ShouldBe(section);

    [Theory]
    [InlineData("salt", null, true)]
    [InlineData("ground cumin", "Spices", true)]
    [InlineData("Olive Oil", "Oils", true)]
    [InlineData("butter", "Dairy", false)]
    public void The_starter_pantry(string name, string? category, bool pantry) => ShoppingDefaults.IsPantry(name, category).ShouldBe(pantry);

    [Fact]
    public void Packages_for_butter_beans_tomatoes_and_eggs()
    {
        ShoppingDefaults.PackageFor("butter").ShouldBe(new PackageDefault(0.5m, "cup", "stick"));
        ShoppingDefaults.PackageFor("diced tomatoes").ShouldBe(new PackageDefault(14.5m, "oz", "can"));
        ShoppingDefaults.PackageFor("egg").ShouldBe(new PackageDefault(12m, null, "carton"));
        ShoppingDefaults.PackageFor("kale").ShouldBeNull();
        ShoppingDefaults.Sections.ShouldContain("Produce");
    }

    private static ParsedIngredient P(decimal? q, string? unit, decimal? max = null) =>
        new() { RawText = "x", Quantity = q, QuantityMax = max, UnitCode = unit, Name = "x" };

    [Fact]
    [Trait("Req", "AC-35")]
    public void Line_amounts_are_base_units_at_the_scale_and_purchase_grams_win()
    {
        ShoppingRules.Amount(P(2m, "cup"), 1.5m).ShouldBe((UnitKind.Volume, 3m * 236.5882365m, (decimal?)null, (string?)null));
        ShoppingRules.Amount(P(1m, "lb", 2m), 1m).ShouldBe((UnitKind.Mass, 453.59237m, (decimal?)907.18474m, (string?)null));
        ShoppingRules.Amount(P(2m, "can"), 2m).ShouldBe((UnitKind.Count, 4m, (decimal?)null, (string?)"can"));
        ShoppingRules.Amount(P(3m, null), 1m).ShouldBe((UnitKind.Count, 3m, (decimal?)null, (string?)null));
        ShoppingRules.Amount(P(1m, "cup"), 1m, P(2m, "tbsp")).Amount.ShouldBe(236.5882365m + 2 * 14.78676478125m);
        ShoppingRules.Amount(P(1m, "cup"), 1m, P(2m, "oz")).Amount.ShouldBe(236.5882365m);
        ShoppingRules.Amount(P(4m, "cup"), 1m, purchaseGrams: 600m, purchaseGramsMax: 750m).ShouldBe((UnitKind.Mass, 600m, (decimal?)750m, (string?)null));
        ShoppingRules.Amount(P(4m, "cup"), 1m, purchaseGrams: 600m, purchaseGramsMax: 600m).AmountMax.ShouldBeNull();
        ShoppingRules.Amount(P(1m, "pinch"), 1m).Kind.ShouldBeNull();
        ShoppingRules.Amount(P(1m, "zzz"), 1m).Kind.ShouldBeNull();
        ShoppingRules.Amount(P(null, "cup"), 1m).Amount.ShouldBeNull();
        ShoppingRules.Amount(P(1m, "cup"), 0m).Amount.ShouldBeNull();
    }
}
