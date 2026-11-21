using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Search;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Web.Pages.Manage;

/// <summary>The editors' starting point (UX-11 "Manage" menu). Editor-only by folder policy.</summary>
public sealed class IndexModel(RecipeSearchService search, RecipeService recipes) : PageModel
{
    public int DraftCount { get; private set; }
    public int TrashCount { get; private set; }

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        DraftCount = (await search.SearchAsync(SearchCriteria.Parse(new SearchInput()), RecipeVisibility.Drafts, ct)).Total;
        TrashCount = (await recipes.ListTrashAsync(ct)).Count;
    }
}
