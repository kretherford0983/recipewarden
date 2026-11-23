using Microsoft.AspNetCore.Mvc;
using RecipeWarden.Infrastructure.Identity;
using RecipeWarden.Infrastructure.Measurement;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Recipes;

/// <summary>
/// FR-EXP-01, AC-24, AC-12, ADR 0031: the print layout at the reading view's scale, units and weights. One column,
/// ingredients before steps, no navigation, source credit with the URL written out. Anonymous like the recipe.
/// </summary>
public sealed class PrintModel(RecipeService recipes, Permissions permissions, RecipeScaler scaler, UserPreferenceStore preferences)
    : RecipeViewModel(recipes, permissions, scaler, preferences)
{
    protected override string RouteSuffix => "/print";

    public async Task<IActionResult> OnGetAsync([FromRoute] string slug, string? scale, string? servings, string? units, int? from, string? have) =>
        await LoadAsync(slug, scale, servings, units, from, have) ?? Page();
}
