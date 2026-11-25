using RecipeWarden.Importers.Ingredients;

namespace RecipeWarden.Importers.Tests;

/// <summary>FR-REC-03, section 8.1: amounts in every common form, with exact fraction text kept.</summary>
[Trait("Req", "FR-REC-03")]
public sealed class FR_REC_03_QuantityParserTests
{
    [Theory]
    [InlineData("1 cup x", 1.0, null)]
    [InlineData("1.5 cup x", 1.5, null)]
    [InlineData(".5 cup x", 0.5, null)]
    [InlineData("3/4 cup x", 0.75, "3/4")]
    [InlineData("1 1/2 cup x", 1.5, "1 1/2")]
    [InlineData("1-1/2 cup x", 1.5, "1 1/2")]
    [InlineData("½ cup x", 0.5, "1/2")]
    [InlineData("1½ cup x", 1.5, "1 1/2")]
    [InlineData("1 ½ cup x", 1.5, "1 1/2")]
    [InlineData("⅛ tsp x", 0.125, "1/8")]
    [InlineData("1⁄3 cup x", 0.333333, "1/3")]
    [InlineData("2 ⅔ cups x", 2.666667, "2 2/3")]
    public void Single_amounts(string line, double quantity, string? text)
    {
        var p = IngredientLineParser.Parse(line);
        p.Quantity.ShouldBe((decimal)quantity);
        p.QuantityMax.ShouldBeNull();
        p.QuantityText.ShouldBe(text);
    }

    [Theory]
    [InlineData("2-3 cups x", 2.0, 3.0, null)]
    [InlineData("2 - 3 cups x", 2.0, 3.0, null)]
    [InlineData("2–3 cups x", 2.0, 3.0, null)]
    [InlineData("2—3 cups x", 2.0, 3.0, null)]
    [InlineData("2 to 3 cups x", 2.0, 3.0, null)]
    [InlineData("2 or 3 cups x", 2.0, 3.0, null)]
    [InlineData("1/2-3/4 cup x", 0.5, 0.75, "1/2 - 3/4")]
    [InlineData("1 to 1 1/2 cups x", 1.0, 1.5, "1 - 1 1/2")]
    [InlineData("1–1½ cups x", 1.0, 1.5, "1 - 1 1/2")]
    [InlineData("¼-½ tsp x", 0.25, 0.5, "1/4 - 1/2")]
    public void Ranges(string line, double min, double max, string? text)
    {
        var p = IngredientLineParser.Parse(line);
        p.Quantity.ShouldBe((decimal)min);
        p.QuantityMax.ShouldBe((decimal)max);
        p.QuantityText.ShouldBe(text);
    }

    [Theory]
    [InlineData("one onion", 1)]
    [InlineData("two eggs", 2)]
    [InlineData("twelve rolls", 12)]
    [InlineData("an egg", 1)]
    public void Number_words(string line, int quantity)
    {
        IngredientLineParser.Parse(line).Quantity.ShouldBe(quantity);
    }

    [Fact]
    public void A_alone_is_a_name_not_an_amount()
    {
        IngredientLineParser.Parse("a").Quantity.ShouldBeNull();
    }

    [Fact]
    public void Numbers_after_the_start_are_not_amounts()
    {
        var p = IngredientLineParser.Parse("2 lb. 80/20 ground beef");
        p.Quantity.ShouldBe(2m);
        p.UnitCode.ShouldBe("lb");
        p.Name.ShouldBe("80/20 ground beef");
    }

    [Theory]
    [InlineData("999999999999999999999999999999 cups flour")]
    [InlineData("1/0 cup sugar")]
    [InlineData("1/99999 cup sugar")]
    public void Absurd_amounts_are_not_taken(string line)
    {
        IngredientLineParser.Parse(line).Quantity.ShouldBeNull();
    }
}
