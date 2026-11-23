using Microsoft.AspNetCore.Mvc;
using RecipeWarden.Infrastructure.Identity;
using RecipeWarden.Infrastructure.Measurement;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Infrastructure.Shopping;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Recipes;

/// <summary>
/// FR-VIEW-01 reading view (see <see cref="RecipeViewModel"/>). Editors also get collections to add to, the shopping
/// lists to add the recipe to (FR-SHOP-01) and the "I made this" note.
/// </summary>
public sealed class DetailsModel(RecipeService recipes, CollectionService collections, ShoppingService shopping, Permissions permissions,
    RecipeScaler scaler, UserPreferenceStore preferences) : RecipeViewModel(recipes, permissions, scaler, preferences)
{
    public IReadOnlyList<CollectionSummary> AvailableCollections { get; private set; } = [];
    public IReadOnlyList<ShoppingListSummary> ShoppingLists { get; private set; } = [];
    public bool JustCooked { get; private set; }

    /// <summary>"Added to Saturday" after adding to a shopping list.</summary>
    [FromQuery(Name = "added")]
    public int? AddedTo { get; set; }

    public ShoppingListSummary? AddedList => ShoppingLists.FirstOrDefault(l => l.Id == AddedTo);

    public async Task<IActionResult> OnGetAsync([FromRoute] string slug, string? scale, string? servings, string? units,
        int? from, string? have, string? made)
    {
        if (await LoadAsync(slug, scale, servings, units, from, have) is { } result)
        {
            return result;
        }

        var ct = HttpContext.RequestAborted;
        JustCooked = made == "1" && CanEdit;
        if (CanEdit)
        {
            var inThese = Recipe!.Collections.Select(c => c.Id).ToHashSet();
            AvailableCollections = (await collections.ListAsync(ct)).Where(c => !inThese.Contains(c.Id)).ToList();
            ShoppingLists = await shopping.ListAsync(ct);
        }

        return Page();
    }
}
