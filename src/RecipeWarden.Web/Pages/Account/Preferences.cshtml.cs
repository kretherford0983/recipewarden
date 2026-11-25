using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Infrastructure.Identity;

namespace RecipeWarden.Web.Pages.Account;

/// <summary>
/// FR-UI-02: saves a signed-in user's unit system and default scale to their profile (POST only, like the theme,
/// FR-UI-01). Anonymous readers keep the choice in a cookie set by recipe.js.
/// </summary>
public sealed class PreferencesModel(UserPreferenceStore preferences) : PageModel
{
    [BindProperty]
    public string? Units { get; set; }

    [BindProperty]
    public string? Scale { get; set; }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!DisplayUnitsRules.TryParse(Units, out var units))
        {
            return BadRequest();
        }

        decimal? scale = null;
        if (!string.IsNullOrWhiteSpace(Scale))
        {
            if (!ScaleRules.TryParseNumber(Scale, out var s) || !ScaleRules.InRange(s))
            {
                return BadRequest();
            }

            scale = s;
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return Unauthorized();
        }

        await preferences.SetDisplayAsync(userId, units, scale, HttpContext.RequestAborted);
        return new NoContentResult();
    }
}
