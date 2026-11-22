using RecipeWarden.Core.Measurement;

namespace RecipeWarden.Core.Tests;

/// <summary>ADR 0013, FR-MEAS-01: ingredients link to foods by exact name or alias only, never by substring.</summary>
[Trait("Req", "FR-MEAS-01")]
public sealed class FR_MEAS_01_FoodMatcherTests
{
    private static readonly FoodMatcher Matcher = new(
    [
        new FoodNames(1, "all-purpose flour", "all-purpose flour", ["flour", "ap flour"]),
        new FoodNames(2, "butter", "butter", ["unsalted butter"]),
        new FoodNames(3, "peanut butter", "peanut butter", []),
        new FoodNames(4, "egg", "eggs", ["large egg"]),
        new FoodNames(5, "chicken breast", "chicken breasts", ["chicken"]),
        new FoodNames(6, "tomato", "tomatoes", []),
        new FoodNames(7, "blueberries", "blueberries", []),
        new FoodNames(8, "jalapeño", "jalapeños", []),
        new FoodNames(9, "molasses", "molasses", []),
    ]);

    [Theory]
    [InlineData("AP flour", 1)]
    [InlineData("all-purpose flour", 1)]
    [InlineData("All-Purpose Flour", 1)]
    [InlineData("flour", 1)]
    [InlineData("all-purpose flour (sifted)", 1)]
    [InlineData("unsalted butter", 2)]
    [InlineData("softened butter", 2)]
    [InlineData("peanut butter", 3)]
    [InlineData("eggs", 4)]
    [InlineData("large eggs", 4)]
    [InlineData("shredded chicken", 5)]
    [InlineData("boneless skinless chicken breasts", 5)]
    [InlineData("tomatoes", 6)]
    [InlineData("fresh blueberries", 7)]
    [InlineData("blueberry", 7)]
    [InlineData("jalapeños", 8)]
    [InlineData("molasses", 9)]
    public void Exact_names_aliases_and_plurals_match(string name, int expected)
    {
        Matcher.Match(name).ShouldBe(expected);
    }

    [Theory]
    [InlineData("cherry tomatoes")]
    [InlineData("chicken broth")]
    [InlineData("egg noodles")]
    [InlineData("almond butter")]
    [InlineData("flour tortillas")]
    [InlineData("")]
    [InlineData(null)]
    public void Nothing_matches_by_substring(string? name)
    {
        Matcher.Match(name).ShouldBeNull();
    }

    [Theory]
    [InlineData("berries", "berry")]
    [InlineData("tomatoes", "tomato")]
    [InlineData("peaches", "peach")]
    [InlineData("eggs", "egg")]
    [InlineData("glass", "glass")]
    [InlineData("couscous", "couscous")]
    [InlineData("molasses", "molasses")]
    [InlineData("large eggs", "large egg")]
    public void Singular_of_the_last_word(string plural, string singular)
    {
        FoodMatcher.Singular(plural).ShouldBe(singular);
    }

    [Theory]
    [InlineData("onion", "onions")]
    [InlineData("potato", "potatoes")]
    [InlineData("cherry", "cherries")]
    [InlineData("eggs", "eggs")]
    [InlineData("peach", "peaches")]
    [InlineData("molasses", "molasses")]
    public void Plural_of_the_last_word(string word, string plural)
    {
        FoodMatcher.Plural(word).ShouldBe(plural);
    }

    [Fact]
    public void A_name_claimed_twice_keeps_the_first_food()
    {
        var matcher = new FoodMatcher([new FoodNames(1, "a", null, ["shared"]), new FoodNames(2, "b", null, ["shared"])]);
        matcher.Match("shared").ShouldBe(1);
    }
}
