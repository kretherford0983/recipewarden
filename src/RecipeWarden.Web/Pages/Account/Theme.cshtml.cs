using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Preferences;
using RecipeWarden.Infrastructure.Identity;

namespace RecipeWarden.Web.Pages.Account;

/// <summary>FR-UI-01: saves a signed-in user's theme to their profile. Anonymous visitors use the cookie only.</summary>
public sealed class ThemeModel(UserPreferenceStore preferences) : PageModel
{
    [BindProperty]
    public string? Theme { get; set; }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ThemeRules.TryParseStrict(Theme, out var theme))
        {
            return BadRequest();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return Unauthorized();
        }

        await preferences.SetThemeAsync(userId, theme, HttpContext.RequestAborted);
        return new NoContentResult();
    }
}
