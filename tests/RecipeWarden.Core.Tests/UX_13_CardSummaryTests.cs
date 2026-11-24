using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>UX-13, FR-SRCH-02: recipe cards show the description as plain text, not its Markdown, cut at a word.</summary>
[Trait("Req", "UX-13")]
[Trait("Req", "FR-SRCH-02")]
public sealed class UX_13_CardSummaryTests
{
    [Theory]
    [InlineData("A dependable recipe with **big flavour**.", "A dependable recipe with big flavour.")]
    [InlineData("Weeknight __favourite__ and *quick*", "Weeknight favourite and quick")]
    [InlineData("See [the blog](https://example.com/x) for more", "See the blog for more")]
    [InlineData("## Heading\nFirst line\n- item one\n- item two", "Heading First line item one item two")]
    [InlineData("Use `kosher salt` > table salt", "Use kosher salt > table salt")]
    [InlineData("1. Prep\n2. Cook", "Prep Cook")]
    [InlineData("~~old~~ new", "old new")]
    [InlineData("snake_case_name stays", "snake_case_name stays")]
    [InlineData("2 * 3 = 6", "2 * 3 = 6")]
    public void Markdown_becomes_plain_text(string markdown, string expected) =>
        CardSummary.From(markdown).ShouldBe(expected);

    [Fact]
    public void Nothing_to_show_is_null()
    {
        CardSummary.From(null).ShouldBeNull();
        CardSummary.From("   \n ").ShouldBeNull();
        CardSummary.From("**  **").ShouldBeNull();
    }

    [Fact]
    public void Long_text_is_cut_at_a_word_with_an_ellipsis()
    {
        var text = string.Join(' ', Enumerable.Repeat("tomato", 40));
        var summary = CardSummary.From(text)!;
        summary.Length.ShouldBeLessThanOrEqualTo(CardSummary.MaxLength + 1);
        summary.ShouldEndWith("tomato…");
    }
}
