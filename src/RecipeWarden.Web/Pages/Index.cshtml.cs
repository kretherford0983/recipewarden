using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Security;

namespace RecipeWarden.Web.Pages;

/// <summary>FR-HOME-01 placeholder (M1 fills it). Anonymous per section 7.2.</summary>
public sealed class IndexModel(IAuthorizationService authorization) : PageModel
{
    public bool CanEdit { get; private set; }

    public async Task OnGetAsync()
    {
        CanEdit = (await authorization.AuthorizeAsync(User, AppPolicies.CanEditRecipes)).Succeeded;
    }
}
