using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages;

/// <summary>
/// FR-HOME-01 (first part): search, recently added, collections, browse by tag. Recommendations (FR-HOME-02)
/// come later. Anonymous per section 7.2. UX-09: empty sections are not rendered.
/// </summary>
public sealed class IndexModel(RecipeSearchService search, CollectionService collections, TagService tags, Permissions permissions)
    : PageModel
{
    public const int RecentCount = 8;

    public bool CanEdit { get; private set; }
    public IReadOnlyList<RecipeCard> Recent { get; private set; } = [];
    public IReadOnlyList<CollectionSummary> Collections { get; private set; } = [];
    public IReadOnlyList<TagSummary> Tags { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        CanEdit = await permissions.CanEditAsync(User);
        Recent = (await search.RecentAsync(ct)).Items.Take(RecentCount).ToList();
        Collections = (await collections.ListAsync(ct)).Where(c => c.RecipeCount > 0).ToList();
        Tags = (await tags.ListAsync(cancellationToken: ct)).Where(t => t.RecipeCount > 0).ToList();
    }
}
