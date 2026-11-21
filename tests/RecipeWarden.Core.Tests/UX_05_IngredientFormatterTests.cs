using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>UX-05: fractions show as glyphs (½, 1¼) and units are abbreviated consistently in the reading view.</summary>
[Trait("Req", "UX-05")]
[Trait("Req", "FR-VIEW-01")]
public sealed class UX_05_IngredientFormatterTests
{
    private static ParsedIngredient P(decimal? q, string? unit, string name, decimal? max = null, string? text = null,
        string? prep = null, string? note = null, bool optional = false, decimal confidence = 1m) => new()
    {
        RawText = "raw text",
        Quantity = q,
        QuantityMax = max,
        QuantityText = text,
        UnitCode = unit,
        Name = name,
        Preparation = prep,
        Note = note,
        IsOptional = optional,
        Confidence = confidence,
    };

    [Theory]
    [InlineData(0.5, "½")]
    [InlineData(0.25, "¼")]
    [InlineData(0.75, "¾")]
    [InlineData(0.333333, "⅓")]
    [InlineData(0.666667, "⅔")]
    [InlineData(0.125, "⅛")]
    [InlineData(0.375, "⅜")]
    [InlineData(1.5, "1½")]
    [InlineData(2.25, "2¼")]
    [InlineData(3, "3")]
    [InlineData(1.2, "1.2")]
    [InlineData(250, "250")]
    public void Amounts_use_fraction_glyphs(double value, string expected)
    {
        IngredientFormatter.Amount((decimal)value).ShouldBe(expected);
    }

    [Fact]
    public void Full_line_reads_naturally()
    {
        IngredientFormatter.Format(P(1.5m, "cup", "all-purpose flour", prep: "sifted"))
            .ShouldBe("1½ cups all-purpose flour, sifted");
    }

    [Fact]
    public void Singular_unit_for_one_or_less()
    {
        IngredientFormatter.Format(P(1m, "cup", "sugar")).ShouldBe("1 cup sugar");
        IngredientFormatter.Format(P(0.5m, "cup", "sugar")).ShouldBe("½ cup sugar");
    }

    [Fact]
    public void Abbreviated_units_do_not_pluralize()
    {
        IngredientFormatter.Format(P(2m, "tbsp", "butter")).ShouldBe("2 Tbsp butter");
        IngredientFormatter.Format(P(3m, "tsp", "salt")).ShouldBe("3 tsp salt");
        IngredientFormatter.Format(P(200m, "g", "flour")).ShouldBe("200 g flour");
    }

    [Fact]
    public void Ranges_notes_and_optional()
    {
        IngredientFormatter.Format(P(2m, "tbsp", "water", max: 3m)).ShouldBe("2–3 Tbsp water");
        IngredientFormatter.Format(P(1m, "can", "black beans", note: "15 oz", optional: true))
            .ShouldBe("1 can black beans (15 oz) (optional)");
        IngredientFormatter.Format(P(null, null, "salt", note: "to taste")).ShouldBe("salt, to taste");
    }

    [Fact]
    public void Counts_without_unit()
    {
        IngredientFormatter.Format(P(2m, null, "large eggs")).ShouldBe("2 large eggs");
    }

    [Fact]
    public void Doubtful_lines_show_the_original_text()
    {
        IngredientFormatter.Format(P(1m, "cup", "x", confidence: 0.5m)).ShouldBe("raw text");
    }

    [Fact]
    public void Unknown_unit_code_shows_the_code()
    {
        IngredientFormatter.Format(P(1m, "zz", "thing")).ShouldBe("1 zz thing");
    }
}
