using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Pages.Listing;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages;

/// <summary>FR-SRCH-01/02, SEC-21: full-text search with filters. Anonymous (7.2); rate-limited per IP (SEC-63).</summary>
public sealed class SearchModel(RecipeSearchService search, TagService tags, CollectionService collections, Permissions permissions)
    : RecipeListPageModel(search, tags, collections, permissions)
{
    public override string ListPath => "/search";

    public Task OnGetAsync() => LoadAsync();
}
