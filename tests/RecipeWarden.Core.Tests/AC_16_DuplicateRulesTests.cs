using RecipeWarden.Core.Import;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-IMP-07, AC-16: likely duplicates by source address, or same title and most ingredients.</summary>
[Trait("Req", "AC-16")]
[Trait("Req", "FR-IMP-07")]
public sealed class AC_16_DuplicateRulesTests
{
    private static readonly DuplicateCandidate Soup = new(7, "Chicken Tortilla Soup", "https://www.example.com/soup/?utm_source=x",
        ["chicken breast", "yellow onion", "tortillas", "lime", "cilantro"]);

    [Theory]
    [InlineData("https://www.example.com/soup/?utm_source=x", "example.com/soup")]
    [InlineData("http://EXAMPLE.com/soup#comments", "example.com/soup")]
    [InlineData("https://example.com:443/soup?b=2&a=1&fbclid=z", "example.com/soup?a=1&b=2")]
    [InlineData("https://example.com/", "example.com")]
    [InlineData("ftp://example.com/soup", null)]
    [InlineData("not a url", null)]
    [InlineData(null, null)]
    public void Duplicate_keys_ignore_noise(string? url, string? key)
    {
        ImportRules.DuplicateKey(url).ShouldBe(key);
    }

    [Fact]
    public void Same_page_is_a_duplicate_whatever_the_title()
    {
        ImportRules.FindDuplicate("Different", "http://example.com/soup", [], [Soup]).ShouldBe(Soup);
    }

    [Fact]
    public void Same_title_with_more_than_80_percent_overlap_is_a_duplicate()
    {
        ImportRules.FindDuplicate("chicken  tortilla soup", null,
            ["Chicken Breast", "yellow onion", "tortillas", "lime", "cilantro"], [Soup]).ShouldBe(Soup);
    }

    [Fact]
    public void Same_title_with_80_percent_or_less_is_not()
    {
        ImportRules.FindDuplicate("Chicken Tortilla Soup", null,
            ["chicken breast", "yellow onion", "tortillas", "lime", "jalapeño"], [Soup]).ShouldBeNull();
        ImportRules.FindDuplicate("Other Soup", null, Soup.IngredientNames, [Soup]).ShouldBeNull();
        ImportRules.FindDuplicate(null, null, Soup.IngredientNames, [Soup]).ShouldBeNull();
    }

    [Fact]
    public void Overlap_is_shared_items_over_the_larger_list()
    {
        ImportRules.IngredientOverlap(["a", "b"], ["a", "b", "c", "d"]).ShouldBe(0.5);
        ImportRules.IngredientOverlap([], ["a"]).ShouldBe(0);
    }
}
