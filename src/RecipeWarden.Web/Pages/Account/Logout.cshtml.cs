using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Web.Pages.Account;

/// <summary>Sign-out is POST only (SEC-31: GET never changes state).</summary>
public sealed class LogoutModel(SignInManager<AppUser> signInManager) : PageModel
{
    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        await signInManager.SignOutAsync();
        return LocalRedirect("/");
    }
}
