using RecipeWarden.Core.Nutrition;
using RecipeWarden.Importers.Import;

namespace RecipeWarden.Importers.Tests;

/// <summary>ADR 0035, AC-55: a page's schema.org nutrition is kept as the imported panel.</summary>
[Trait("Req", "AC-55")]
public sealed class FR_NUT_01_ImportedNutritionTests
{
    private const string Page = """
        <html><head><script type="application/ld+json">
        {"@context":"https://schema.org","@type":"Recipe","name":"Soup",
         "recipeIngredient":["1 cup milk"],"recipeInstructions":["Heat."],
         "nutrition":{"@type":"NutritionInformation","calories":"410 kcal","fatContent":"14 g","sodiumContent":"890 mg",
                      "proteinContent":"32 g","cholesterolContent":"0.095 g"}}
        </script></head><body></body></html>
        """;

    [Fact]
    public void Json_ld_nutrition_is_read_per_serving()
    {
        var recipe = HtmlRecipeExtractor.Extract(Page, new Uri("https://example.com/soup"))!;
        recipe.Nutrition.ShouldBe(new NutrientValues(410, 14, null, 95, 890, null, null, null, 32));
    }

    [Fact]
    public void A_page_without_calories_has_no_panel()
    {
        var recipe = HtmlRecipeExtractor.Extract(Page.Replace("\"calories\":\"410 kcal\",", "", StringComparison.Ordinal), null)!;
        recipe.Nutrition.ShouldBeNull();
        HtmlRecipeExtractor.Extract(Page.Replace("\"nutrition\":{", "\"x\":{", StringComparison.Ordinal), null)!.Nutrition.ShouldBeNull();
    }
}
