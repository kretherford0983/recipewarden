using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Collections;

/// <summary>FR-ORG-02: collection index, visible to every reader (7.2).</summary>
public sealed class IndexModel(CollectionService collections, Permissions permissions) : PageModel
{
    public IReadOnlyList<CollectionSummary> Collections { get; private set; } = [];
    public bool CanEdit { get; private set; }

    public async Task OnGetAsync()
    {
        Collections = await collections.ListAsync(HttpContext.RequestAborted);
        CanEdit = await permissions.CanEditAsync(User);
    }
}
