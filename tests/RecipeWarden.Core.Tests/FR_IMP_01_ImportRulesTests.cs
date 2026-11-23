using RecipeWarden.Core.Import;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-IMP-01, FR-IMP-06, BR-04, BR-13: shared import rules.</summary>
[Trait("Req", "FR-IMP-01")]
public sealed class FR_IMP_01_ImportRulesTests
{
    [Theory]
    [InlineData("PT15M", "15")]
    [InlineData("PT1H15M", "75")]
    [InlineData("pt1h", "60")]
    [InlineData("P0DT2H30M", "150")]
    [InlineData("PT1.5H", "90")]
    [InlineData("PT90S", "2")]
    [InlineData("P1D", "1440")]
    [InlineData("20 min", "20 min")]
    [InlineData("PT0M", null)]
    [InlineData("P", null)]
    [InlineData("soon", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Times_read_iso_durations(string? value, string? minutes)
    {
        ImportRules.Minutes(value).ShouldBe(minutes);
    }

    [Theory]
    [InlineData("4", "4", null, null)]
    [InlineData("Serves 6-8", "6-8", null, null)]
    [InlineData("6 to 8 servings", "6-8", null, null)]
    [InlineData("4 servings", "4", null, null)]
    [InlineData("Makes 12 fl oz", null, "12 fl oz", null)]
    [InlineData("2 quarts", null, "2 quarts", null)]
    [InlineData("24 cookies", null, null, "Makes 24 cookies")]
    public void Yields_become_servings_amounts_or_a_remark(string value, string? servings, string? yield, string? remark)
    {
        ImportRules.ServingsAndYield([value]).ShouldBe((servings, yield, remark));
    }

    [Fact]
    public void The_first_servings_and_the_first_amount_win()
    {
        ImportRules.ServingsAndYield(["8", "8 servings", "<b>1 quart</b>", null]).ShouldBe(("8", "1 quart", (string?)null));
    }

    [Fact]
    [Trait("Req", "BR-04")]
    [Trait("Req", "FR-IMP-06")]
    public void Warnings_list_missing_parts_and_low_confidence_lines()
    {
        var recipe = new ImportedRecipe { Ingredients = "1 cup flour\nsome stuff", Remarks = ["Makes 24 cookies"] };
        var warnings = ImportRules.Warnings(recipe, [("1 cup flour", 0.95), ("some stuff", 0.5)]);

        warnings.ShouldBe(
        [
            new ImportWarning(ImportRules.NoTitle),
            new ImportWarning(ImportRules.NoSteps),
            new ImportWarning(ImportRules.CheckLine, "some stuff"),
            new ImportWarning("Makes 24 cookies"),
        ]);
        new ImportedRecipe { Title = "Soup", Ingredients = "x", Steps = "y" }.HasContent.ShouldBeTrue();
        new ImportedRecipe { Title = "Soup" }.HasContent.ShouldBeFalse();
        ImportRules.Warnings(new ImportedRecipe { Title = "Soup", Steps = "Stir." }, []).ShouldBe([new ImportWarning(ImportRules.NoIngredients)]);
    }

    [Fact]
    [Trait("Req", "BR-13")]
    public void Imported_text_is_plain_text()
    {
        ImportText.Line("<p>Rich &amp; <b>creamy</b></p>").ShouldBe("Rich & creamy");
        ImportText.Line("Bake at 350&deg;F &amp;frac12; hour").ShouldBe("Bake at 350°F ½ hour");
        ImportText.Line("Soup<script>alert(1)</script> time").ShouldBe("Soup time");
        ImportText.Line("&lt;script&gt;alert(1)&lt;/script&gt;").ShouldBe("<script>alert(1)</script>");
        ImportText.Block("Line one<br>Line two\r\n\r\n\r\n\r\nLine three\u0007").ShouldBe("Line one\nLine two\n\nLine three");
        ImportText.Line("  \u00a0 ").ShouldBeNull();
        ImportText.Line(new string('a', 600), 500)!.Length.ShouldBe(500);
        ImportText.Lines(["<li>1 cup flour</li>", " ", null, "2 eggs"]).ShouldBe(["1 cup flour", "2 eggs"]);
    }
}
