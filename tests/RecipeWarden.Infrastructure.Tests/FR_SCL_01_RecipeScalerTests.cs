using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>FR-SCL-01/02/03, FR-MEAS-01/04/07: a stored recipe read at a scale and in a unit system (ADR 0014).</summary>
[Trait("Req", "FR-SCL-01")]
public sealed class FR_SCL_01_RecipeScalerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<RecipeView> SaveAsync(RecipeTestScope s, string ingredients, string steps = "Mix.", decimal servings = 4m,
        bool precision = false)
    {
        var edit = RecipeTestScope.Edit("Test Recipe", ingredients, steps) with { Servings = servings, IsPrecision = precision };
        var saved = await s.Recipes.CreateAsync(edit, RecipeTestScope.UserId, Ct);
        saved.Status.ShouldBe(SaveStatus.Saved);
        return (await s.Recipes.GetForReadingAsync(saved.Slug!, canSeeDrafts: true, Ct)).Recipe!;
    }

    private static IEnumerable<string> Texts(Measurement.ScaledRecipe r) => r.Ingredients.SelectMany(g => g.Lines).Select(l => l.Text);

    [Fact]
    [Trait("Req", "AC-30")]
    public async Task Scaling_multiplies_amounts_and_servings_and_leaves_the_stored_recipe_unchanged()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var view = await SaveAsync(s, "2 cups milk\n1 lb ground beef\nsalt to taste");
        var before = await s.Db.RecipeIngredients.AsNoTracking().OrderBy(i => i.Id).Select(i => new { i.Quantity, i.UnitId, i.RawText }).ToListAsync(Ct);
        var version = await s.Db.Recipes.AsNoTracking().Select(r => r.Version).SingleAsync(Ct);

        foreach (var (factor, servings) in new[] { (1.5m, 6m), (2m, 8m), (10m, 40m) })
        {
            var scaled = await s.Scaler.ScaleAsync(view, factor, DisplayUnits.AsWritten, Ct);
            scaled.Servings.ShouldBe(servings);
        }

        var tenX = await s.Scaler.ScaleAsync(view, 10m, DisplayUnits.AsWritten, Ct);
        Texts(tenX).ShouldBe(["20 cups milk", "10 lb ground beef", "salt, to taste"]);
        tenX.TimeNote.ShouldBe(ScaleRules.TimeNote);

        (await s.Db.RecipeIngredients.AsNoTracking().OrderBy(i => i.Id).Select(i => new { i.Quantity, i.UnitId, i.RawText }).ToListAsync(Ct))
            .ShouldBe(before);
        (await s.Db.Recipes.AsNoTracking().Select(r => r.Version).SingleAsync(Ct)).ShouldBe(version);
    }

    [Fact]
    [Trait("Req", "AC-31")]
    public async Task Scale_from_three_pounds_of_chicken_gives_2x()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var view = await SaveAsync(s, "1 cup rice\n1.5 lb boneless skinless chicken breasts");

        var result = await s.Scaler.FactorFromAsync(view, 1, "3 lb", Ct);

        result.Factor.ShouldBe(2m);
        (await s.Scaler.FactorFromAsync(view, 0, "500 g", Ct)).Factor!.Value.ShouldBe(2.7m, 0.05m); // rice has a density
        (await s.Scaler.FactorFromAsync(view, 9, "3 lb", Ct)).Error.ShouldBe(IngredientFactor.NoAmount);
        (await s.Scaler.FactorFromAsync(view, 1, new string('9', 41), Ct)).Error.ShouldBe(IngredientFactor.HaveNoAmount);
    }

    [Fact]
    [Trait("Req", "AC-35")]
    public async Task Cooked_chicken_shows_the_raw_estimate_from_the_seeded_yield()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var view = await SaveAsync(s, "4 cups cooked shredded chicken");

        var line = (await s.Scaler.ScaleAsync(view, 1m, DisplayUnits.AsWritten, Ct)).Ingredients.Single().Lines.Single();

        line.YieldNote.ShouldBe("About 1½–1¾ lb raw chicken breast to buy (boneless breast, simmered, about 70–88% cooking yield).");
        line.PurchaseGrams.ShouldNotBeNull();
        line.PurchaseGramsMax.ShouldNotBeNull();
    }

    [Fact]
    [Trait("Req", "FR-MEAS-01")]
    public async Task Weight_view_uses_the_seeded_density()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var view = await SaveAsync(s, "2 cups all-purpose flour\n1 cup unobtainium", precision: true);

        var lines = (await s.Scaler.ScaleAsync(view, 1m, DisplayUnits.Weight, Ct)).Ingredients.Single().Lines;

        lines[0].Text.ShouldBe("250 g all-purpose flour");
        lines[0].Secondary.ShouldBe("2 cups");
        lines[1].Text.ShouldBe("1 cup unobtainium");
        lines[1].Hint.ShouldBe(IngredientScaler.NoDensityHint);
    }

    [Fact]
    public async Task Plus_amounts_scale_with_their_line()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var view = await SaveAsync(s, "1 cup plus 2 tablespoons flour");
        Texts(await s.Scaler.ScaleAsync(view, 2m, DisplayUnits.AsWritten, Ct)).ShouldBe(["2¼ cups flour"]);
    }

    [Fact]
    [Trait("Req", "FR-MEAS-07")]
    public async Task Step_temperatures_follow_the_unit_choice_and_headings_are_kept()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var view = await SaveAsync(s, "1 cup flour", "1. **Preheat oven**\n   Preheat oven to **375°F**.\n2. Bake.");

        var metric = await s.Scaler.ScaleAsync(view, 1m, DisplayUnits.Metric, Ct);

        metric.Steps.Single().Steps.ShouldBe([new StepView("Preheat oven", "Preheat oven to 190°C."), new StepView(null, "Bake.")]);
        view.Steps.Single().Steps[0].Text.ShouldBe("Preheat oven to 375°F.");
    }

    [Fact]
    [Trait("Req", "AC-36")]
    public async Task Eggs_follow_the_precision_flag()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var plain = await SaveAsync(s, "3 eggs");
        var baking = await SaveAsync(s, "3 eggs", precision: true);

        Texts(await s.Scaler.ScaleAsync(plain, 1.5m, DisplayUnits.AsWritten, Ct)).ShouldBe(["4 eggs + 1 yolk"]);
        Texts(await s.Scaler.ScaleAsync(baking, 1.5m, DisplayUnits.AsWritten, Ct)).ShouldBe(["4½ eggs (4 eggs + 2 Tbsp beaten egg)"]);
    }
}
