using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Collections;

/// <summary>FR-ORG-02, AC-20: a collection's recipes in order. Anonymous (7.2); drafts shown to editors only.</summary>
public sealed class DetailsModel(CollectionService collections, Permissions permissions) : PageModel
{
    public CollectionView? Collection { get; private set; }
    public bool CanEdit { get; private set; }

    public async Task<IActionResult> OnGetAsync([FromRoute] string slug)
    {
        CanEdit = await permissions.CanEditAsync(User);
        Collection = await collections.GetBySlugAsync(slug, includeDrafts: CanEdit, HttpContext.RequestAborted);
        return Collection is null ? NotFound() : Page();
    }
}
