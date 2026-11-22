using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Importers.Ingredients;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Infrastructure.Measurement;

/// <summary>An ingredient line as the scaled view shows it; <see cref="Index"/> numbers lines across groups.</summary>
public sealed record ScaledLine(int Index, string Text, string RawText, bool NeedsReview, bool CanScaleFrom, string? Secondary,
    string? Hint, string? YieldNote, decimal? PurchaseGrams, decimal? PurchaseGramsMax);

public sealed record ScaledGroup(string? Name, IReadOnlyList<ScaledLine> Lines);

/// <summary>FR-SCL-01/03, FR-MEAS-01 to 07 (M2 part): the reading view at a scale and in a unit system. Never stored.</summary>
public sealed record ScaledRecipe(decimal Factor, DisplayUnits Units, decimal Servings, decimal? ServingsMax,
    IReadOnlyList<ScaledGroup> Ingredients, IReadOnlyList<StepSectionView> Steps, string? TimeNote);

/// <summary>
/// Applies the Core measurement engine (9.4) to a recipe for reading (ADR 0014). Read-only: it never writes, so
/// scaling is available wherever the recipe can be read (FR-SCL-03).
/// </summary>
public sealed class RecipeScaler(FoodCatalogService foods)
{
    public const int MaxHaveLength = 40;

    public async Task<ScaledRecipe> ScaleAsync(RecipeView recipe, decimal factor, DisplayUnits units, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var catalog = await foods.LoadCatalogAsync(cancellationToken).ConfigureAwait(false);
        var context = new ScaleContext(factor, units, recipe.IsPrecision, catalog);
        var index = 0;
        var groups = new List<ScaledGroup>();
        foreach (var group in recipe.Ingredients)
        {
            var lines = new List<ScaledLine>();
            foreach (var line in group.Lines)
            {
                lines.Add(ScaleLine(index++, line, context));
            }

            groups.Add(new ScaledGroup(group.Name, lines));
        }

        var steps = recipe.Steps
            .Select(s => new StepSectionView(s.Name,
                s.Steps.Select(t => new StepView(t.Title, TemperatureText.Convert(t.Text, units))).ToList()))
            .ToList();
        var (servings, servingsMax) = ScaleRules.Servings(recipe.Servings, recipe.ServingsMax, factor);
        return new ScaledRecipe(factor, units, servings, servingsMax, groups, steps, ScaleRules.NoteFor(factor));
    }

    /// <summary>
    /// FR-SCL-02: the factor from "I have <paramref name="have"/>" of the ingredient at <paramref name="lineIndex"/>.
    /// </summary>
    public async Task<IngredientFactorResult> FactorFromAsync(RecipeView recipe, int lineIndex, string? have,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var line = recipe.Ingredients.SelectMany(g => g.Lines).ElementAtOrDefault(lineIndex);
        if (line?.Parsed is not { } recipeLine || lineIndex < 0)
        {
            return new IngredientFactorResult(null, IngredientFactor.NoAmount);
        }

        if (string.IsNullOrWhiteSpace(have) || have.Length > MaxHaveLength)
        {
            return new IngredientFactorResult(null, IngredientFactor.HaveNoAmount);
        }

        // Read "3 lb" with the ingredient parser, as if it were a line of the recipe.
        var parsedHave = IngredientLineParser.Parse(have.Trim() + " " + (recipeLine.Name.Length > 0 ? recipeLine.Name : "x"));
        var catalog = await foods.LoadCatalogAsync(cancellationToken).ConfigureAwait(false);
        return IngredientFactor.Compute(parsedHave, recipeLine, catalog.Food(line.FoodId)?.DensityGPerMl, catalog);
    }

    private static ScaledLine ScaleLine(int index, IngredientLineView line, ScaleContext context)
    {
        if (line.Parsed is not { } parsed)
        {
            return new ScaledLine(index, line.Text, line.RawText, line.NeedsReview, false, null, null, null, null, null);
        }

        var scaled = IngredientScaler.Scale(new ScaleInput(parsed, line.FoodId, PlusAmount(parsed.Note)), context);
        return new ScaledLine(index, scaled.Text, line.RawText, line.NeedsReview, parsed.Quantity is > 0m && !line.NeedsReview,
            scaled.Secondary, scaled.Hint, scaled.YieldNote, scaled.PurchaseGrams, scaled.PurchaseGramsMax);
    }

    /// <summary>"plus 2 tablespoons" → 2 Tbsp, so "1 cup plus 2 tablespoons flour" scales as one amount.</summary>
    internal static ParsedIngredient? PlusAmount(string? note)
    {
        const string Plus = "plus ";
        if (note is null || !note.StartsWith(Plus, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parsed = IngredientLineParser.Parse(note[Plus.Length..].Trim() + " x");
        return parsed.Quantity is not null && parsed.UnitCode is not null ? parsed : null;
    }
}
