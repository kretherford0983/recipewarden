using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Pages.Listing;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Recipes;

/// <summary>FR-SRCH-02 browse grid with filters and sort. Anonymous (7.2); editors can list drafts.</summary>
public sealed class IndexModel(RecipeSearchService search, TagService tags, CollectionService collections, Permissions permissions)
    : RecipeListPageModel(search, tags, collections, permissions)
{
    public override string ListPath => "/recipes";

    public Task OnGetAsync() => LoadAsync();
}
