using RecipeWarden.Importers.Ingredients;

namespace RecipeWarden.Importers.Tests;

/// <summary>FR-REC-01, FR-REC-02 (W2 step 3): pasted ingredients, one per line, with optional group headings.</summary>
[Trait("Req", "FR-REC-01")]
[Trait("Req", "FR-REC-02")]
public sealed class FR_REC_01_IngredientListTests
{
    [Fact]
    public void Lines_and_headings_are_recognized_and_blank_lines_skipped()
    {
        const string text = "For the dough:\n2 cups flour\n\n1 tsp salt\r\nFor the filling\n1 cup ricotta\nTopping:\n";

        var lines = IngredientLineParser.ParseList(text);

        lines.Select(l => l.IsHeading ? "# " + l.GroupHeading : l.Ingredient!.Name).ShouldBe(
            ["# For the dough", "flour", "salt", "# For the filling", "ricotta", "# Topping"]);
    }

    [Theory]
    [InlineData("2 cups flour:")]
    [InlineData("Optional: 1 cup nuts")]
    [InlineData("For serving")]
    [InlineData(":")]
    [InlineData("This is a very long sentence that happens to end with a colon:")]
    public void Lines_that_are_not_headings(string line)
    {
        IngredientLineParser.ParseList(line).Single().IsHeading.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\n\n  \n")]
    public void Empty_text_gives_no_lines(string? text)
    {
        IngredientLineParser.ParseList(text).ShouldBeEmpty();
    }
}
