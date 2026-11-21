using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Recipes;

/// <summary>
/// FR-VIEW-01 reading view. Anonymous (7.2); readers never see edit controls (UX-11) or drafts (ADR 0009).
/// An earlier slug of a published recipe redirects permanently to the current one (BR-02, ADR 0008).
/// </summary>
public sealed class DetailsModel(RecipeService recipes, CollectionService collections, Permissions permissions) : PageModel
{
    public RecipeView? Recipe { get; private set; }
    public bool CanEdit { get; private set; }
    public IReadOnlyList<CollectionSummary> AvailableCollections { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync([FromRoute] string slug)
    {
        var ct = HttpContext.RequestAborted;
        CanEdit = await permissions.CanEditAsync(User);
        var lookup = await recipes.GetForReadingAsync(slug, canSeeDrafts: CanEdit, ct);
        if (lookup.RedirectToSlug is { } current)
        {
            return RedirectPermanent("/recipes/" + current);
        }

        if (lookup.Recipe is null)
        {
            return NotFound();
        }

        Recipe = lookup.Recipe;
        if (CanEdit)
        {
            var inThese = Recipe.Collections.Select(c => c.Id).ToHashSet();
            AvailableCollections = (await collections.ListAsync(ct)).Where(c => !inThese.Contains(c.Id)).ToList();
        }

        return Page();
    }
}
