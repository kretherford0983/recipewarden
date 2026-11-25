using System.Text;
using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Import;
using RecipeWarden.Core.Kitchen;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Import;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>
/// FR-SCL-06, BR-111, BR-133, BR-135, AC-138, AC-141, AC-142 (owner testing 2026-11-25): an import that does not say
/// what it makes gets the ingredient estimate in Makes, saved as an estimate unless the Editor changes it; a stated
/// yield is saved as entered; versions under one title are separate drafts in one batch.
/// </summary>
[Trait("Req", "FR-SCL-06")]
public sealed class BR_135_ImportYieldServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Sauce = """
        # Quick Hot Sauce

        ## Ingredients
        - 1 cup hot sauce
        - 1/3 cup vegetable oil
        - salt to taste

        ## Steps
        1. Whisk together.
        """;

    private static async Task<ImportReview> ImportAsync(RecipeTestScope t, string markdown, string name = "sauce.md", int index = 0)
    {
        var bytes = Encoding.UTF8.GetBytes(markdown);
        var start = await t.Import.StartFilesAsync([new ImportFile(name, new MemoryStream(bytes), bytes.Length)], RecipeTestScope.UserId, Ct);
        start.Error.ShouldBeNull();
        return (await t.Import.GetReviewAsync(start.JobIds[index], Ct))!;
    }

    private static RecipeEdit EditFrom(ImportReview review, string? yield) => new()
    {
        Title = review.Recipe.Title,
        Servings = 4,
        IngredientsText = review.Recipe.Ingredients,
        StepsText = review.Recipe.Steps,
        YieldText = yield,
        SourceType = SourceType.File,
    };

    [Fact]
    [Trait("Req", "AC-141")]
    [Trait("Req", "BR-135")]
    public async Task An_unstated_yield_is_prefilled_and_saved_as_an_estimate()
    {
        await using var t = await RecipeTestScope.CreateAsync();
        var review = await ImportAsync(t, Sauce);

        review.Recipe.Yield.ShouldBe("about 1 1/3 cups");
        review.EstimatedYield.ShouldBe("about 1 1/3 cups");
        review.Warnings.Select(w => w.Message).ShouldContain(ImportYieldRulesText("about 1 1/3 cups"));

        var saved = await t.Import.SaveAsync(review.JobId, EditFrom(review, review.Recipe.Yield), false, false, RecipeTestScope.UserId, Ct);
        saved.Outcome!.Status.ShouldBe(SaveStatus.Saved);
        var recipe = await t.Db.Recipes.AsNoTracking().SingleAsync(r => r.Id == saved.Outcome.Id, Ct);
        recipe.YieldSource.ShouldBe(nameof(YieldSource.Estimated));
        recipe.YieldText.ShouldBe("about 1 1/3 cups");
        recipe.YieldAmount.ShouldNotBeNull().ShouldBe(4m / 3m, 0.001m);
    }

    [Fact]
    [Trait("Req", "AC-141")]
    [Trait("Req", "BR-135")]
    public async Task An_edited_estimate_is_saved_as_entered()
    {
        await using var t = await RecipeTestScope.CreateAsync();
        var review = await ImportAsync(t, Sauce);

        var saved = await t.Import.SaveAsync(review.JobId, EditFrom(review, "12 fl oz"), false, false, RecipeTestScope.UserId, Ct);

        var recipe = await t.Db.Recipes.AsNoTracking().SingleAsync(r => r.Id == saved.Outcome!.Id, Ct);
        recipe.YieldSource.ShouldBe(nameof(YieldSource.Entered));
        recipe.YieldText.ShouldBe("12 fl oz");
    }

    [Fact]
    [Trait("Req", "AC-142")]
    [Trait("Req", "BR-135")]
    public async Task A_dish_whose_amounts_mostly_cannot_be_counted_gets_no_estimate()
    {
        await using var t = await RecipeTestScope.CreateAsync();
        var review = await ImportAsync(t, """
            # Wings

            ## Ingredients
            - 3 lb chicken wings
            - 2 eggs
            - 1/2 cup hot sauce

            ## Steps
            1. Bake.
            """);

        review.Recipe.Yield.ShouldBeNull();
        review.EstimatedYield.ShouldBeNull();
    }

    [Fact]
    [Trait("Req", "AC-138")]
    [Trait("Req", "BR-133")]
    [Trait("Req", "BR-134")]
    public async Task Versions_are_drafts_in_one_batch_and_a_stated_yield_is_saved_as_entered()
    {
        await using var t = await RecipeTestScope.CreateAsync();
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "buffalo-wing-sauce.md"), Ct);
        var start = await t.Import.StartFilesAsync([new ImportFile("buffalo-wing-sauce.md", new MemoryStream(bytes), bytes.Length)], RecipeTestScope.UserId, Ct);

        start.JobIds.Count.ShouldBe(2);
        var butter = (await t.Import.GetReviewAsync(start.JobIds[1], Ct))!;
        butter.Batch.Count.ShouldBe(2);
        butter.Recipe.Yield.ShouldBe("about 3/4 cup");
        butter.EstimatedYield.ShouldBeNull();

        var saved = await t.Import.SaveAsync(butter.JobId, EditFrom(butter, butter.Recipe.Yield), false, false, RecipeTestScope.UserId, Ct);
        var recipe = await t.Db.Recipes.AsNoTracking().SingleAsync(r => r.Id == saved.Outcome!.Id, Ct);
        recipe.YieldSource.ShouldBe(nameof(YieldSource.Entered));
    }

    private static string ImportYieldRulesText(string text) =>
        Core.Import.ImportYieldRules.Warning(new Core.Import.YieldPrefill(text, []));
}
