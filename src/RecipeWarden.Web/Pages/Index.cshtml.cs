using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages;

/// <summary>
/// FR-HOME-01, FR-HOME-02 (ADR 0044): search, recommendations, recently added, collections, browse by tag.
/// Anonymous per section 7.2. UX-09: empty sections are not rendered.
/// </summary>
public sealed class IndexModel(RecipeSearchService search, CollectionService collections, TagService tags, RecommendationService recommendations,
    Permissions permissions)
    : PageModel
{
    public const int RecentCount = 8;

    public bool CanEdit { get; private set; }
    public IReadOnlyList<RecipeCard> Recent { get; private set; } = [];
    public IReadOnlyList<CollectionSummary> Collections { get; private set; } = [];
    public IReadOnlyList<TagSummary> Tags { get; private set; } = [];
    public Recommendations Picks { get; private set; } = new([], [], []);

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        CanEdit = await permissions.CanEditAsync(User);
        Recent = (await search.RecentAsync(ct)).Items.Take(RecentCount).ToList();
        Picks = await recommendations.GetAsync(ct);
        Collections = (await collections.ListAsync(ct)).Where(c => c.RecipeCount > 0).ToList();
        Tags = (await tags.ListAsync(cancellationToken: ct)).Where(t => t.RecipeCount > 0).ToList();
    }
}
