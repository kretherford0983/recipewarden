using RecipeWarden.Core.Recipes;
using RecipeWarden.Importers.Ingredients;

namespace RecipeWarden.Importers.Tests;

/// <summary>FR-REC-03, BR-04, AC-03: golden lines, each checked field by field.</summary>
[Trait("Req", "FR-REC-03")]
[Trait("Req", "BR-04")]
public sealed class FR_REC_03_IngredientLineParserTests
{
    [Fact]
    [Trait("Req", "AC-03")]
    public void AC_03_line_parses_exactly()
    {
        const string line = "1 ½ cups all-purpose flour, sifted";

        var p = IngredientLineParser.Parse(line);

        p.Quantity.ShouldBe(1.5m);
        p.QuantityText.ShouldBe("1 1/2");
        p.UnitCode.ShouldBe("cup");
        p.Name.ShouldBe("all-purpose flour");
        p.Preparation.ShouldBe("sifted");
        p.Confidence.ShouldBeGreaterThanOrEqualTo(0.8m);
        p.NeedsReview.ShouldBeFalse();
        p.RawText.ShouldBe(line);
    }

    [Theory]
    [InlineData("2 cups milk", 2.0, null, "cup", "milk", null)]
    [InlineData("1/2 tsp salt", 0.5, null, "tsp", "salt", null)]
    [InlineData("3 cloves garlic, minced", 3.0, null, "clove", "garlic", "minced")]
    [InlineData("2 large eggs", 2.0, null, null, "large eggs", null)]
    [InlineData("2-3 tablespoons water", 2.0, 3.0, "tbsp", "water", null)]
    [InlineData("1 T honey", 1.0, null, "tbsp", "honey", null)]
    [InlineData("1 t salt", 1.0, null, "tsp", "salt", null)]
    [InlineData("200g butter", 200.0, null, "g", "butter", null)]
    [InlineData("12 fl oz beer", 12.0, null, "floz", "beer", null)]
    [InlineData("a pinch of salt", 1.0, null, "pinch", "salt", null)]
    [InlineData("1 cup chopped onion", 1.0, null, "cup", "onion", "chopped")]
    [InlineData("1 tsp freshly ground black pepper", 1.0, null, "tsp", "black pepper", "freshly ground")]
    [InlineData("1 tsp ground cumin", 1.0, null, "tsp", "ground cumin", null)]
    public void Golden_lines(string line, double qty, double? max, string? unit, string name, string? prep)
    {
        var p = IngredientLineParser.Parse(line);

        p.Quantity.ShouldBe((decimal)qty);
        p.QuantityMax.ShouldBe(max is null ? null : (decimal)max);
        p.UnitCode.ShouldBe(unit);
        p.Name.ShouldBe(name);
        p.Preparation.ShouldBe(prep);
        p.NeedsReview.ShouldBeFalse();
    }

    [Fact]
    public void Package_size_goes_to_the_note()
    {
        var p = IngredientLineParser.Parse("1 (15 oz) can black beans, drained and rinsed");

        p.Quantity.ShouldBe(1m);
        p.UnitCode.ShouldBe("can");
        p.Name.ShouldBe("black beans");
        p.Preparation.ShouldBe("drained and rinsed");
        p.Note.ShouldBe("15 oz");
    }

    [Fact]
    public void Optional_is_flagged_and_removed_from_the_name()
    {
        var p = IngredientLineParser.Parse("1/2 cup walnuts (optional)");
        p.IsOptional.ShouldBeTrue();
        p.Name.ShouldBe("walnuts");
        p.Note.ShouldBeNull();
    }

    [Fact]
    public void To_taste_has_no_amount_and_a_note()
    {
        var p = IngredientLineParser.Parse("Salt and pepper to taste");
        p.Quantity.ShouldBeNull();
        p.UnitCode.ShouldBeNull();
        p.Name.ShouldBe("Salt and pepper");
        p.Note.ShouldBe("to taste");
        p.NeedsReview.ShouldBeFalse();
    }

    [Fact]
    public void State_word_is_separated_for_yield_conversions()
    {
        var p = IngredientLineParser.Parse("4 cups cooked shredded chicken");
        p.State.ShouldBe("cooked");
        p.Preparation.ShouldBe("shredded");
        p.Name.ShouldBe("chicken");
    }

    [Fact]
    public void Plus_amount_is_kept_as_a_note()
    {
        var p = IngredientLineParser.Parse("1 cup plus 2 tablespoons flour");
        p.Quantity.ShouldBe(1m);
        p.UnitCode.ShouldBe("cup");
        p.Name.ShouldBe("flour");
        p.Note.ShouldBe("plus 2 tablespoons");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_line_is_empty_with_zero_confidence(string? line)
    {
        var p = IngredientLineParser.Parse(line);
        p.Name.ShouldBeEmpty();
        p.Confidence.ShouldBe(0m);
        p.NeedsReview.ShouldBeTrue();
    }

    [Theory]
    [InlineData("1 cup")]                       // no ingredient
    [InlineData("2 tbsp fresh or 2 tsp dried thyme")] // a second amount inside the name
    [InlineData("1 cup flour (sifted")]         // unbalanced parenthesis
    [InlineData("1/0 cup sugar")]               // impossible amount
    [InlineData("3 cups of the very best and most expensive imported Italian flour")]
    public void Doubtful_lines_are_flagged_for_review(string line)
    {
        IngredientLineParser.Parse(line).NeedsReview.ShouldBeTrue();
    }

    [Fact]
    public void Raw_text_is_trimmed_but_otherwise_unchanged()
    {
        IngredientLineParser.Parse("  2 Tablespoons  Butter  ").RawText.ShouldBe("2 Tablespoons  Butter");
    }
}
