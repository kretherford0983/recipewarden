using RecipeWarden.Core.Nutrition;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>ADR 0035/0036, AC-55, AC-25: imported nutrition is kept; exports carry allergens and the panel.</summary>
[Trait("Req", "AC-55")]
public sealed class FR_NUT_01_ImportedNutritionStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Pasted = """
        ---
        format: recipe-md/1
        title: "Imported Soup"
        nutrition:
          source: imported
          per_serving: { calories: 410, fat_g: 14, sodium_mg: 890 }
        ---

        # Imported Soup

        ## Ingredients

        - 1 cup whole milk

        ## Steps

        1. Heat.
        """;

    [Fact]
    public async Task A_pasted_recipe_md_panel_is_saved_as_the_imported_panel_and_exported_with_allergens()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var start = await s.Import.StartPasteAsync(Pasted, null, false, RecipeTestScope.UserId, Ct);
        var review = (await s.Import.GetReviewAsync(start.JobIds[0], Ct))!;
        var r = review.Recipe;
        var edit = new RecipeEdit { Title = r.Title, IngredientsText = r.Ingredients, StepsText = r.Steps, WantsPublish = true };
        var saved = await s.Import.SaveAsync(review.JobId, edit, false, false, RecipeTestScope.UserId, Ct);
        var id = saved.Outcome!.Id;

        var n = (await s.Nutrition.GetAsync(id, Ct))!;
        n.Panel!.Source.ShouldBe(NutritionSource.Imported);
        n.Panel.PerServing.ShouldBe(new NutrientValues(410, 14, SodiumMg: 890));

        var md = (await s.Exporter.MarkdownAsync(id, null, Ct))!;
        md.ShouldContain("allergens: [milk]");
        md.ShouldContain("nutrition:\n  source: imported\n  per_serving: { calories: 410, fat_g: 14, sodium_mg: 890 }");
    }
}
