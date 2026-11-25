using System.Security.Claims;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Infrastructure.Identity;

namespace RecipeWarden.Web.Pages.Shopping;

/// <summary>FR-UI-02 for lists: metric when the reader's profile or cookie says metric, else US units.</summary>
public static class ShoppingUnits
{
    public static async Task<UnitSystem> ResolveAsync(HttpContext context, UserPreferenceStore preferences)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(preferences);
        DisplayUnits? profile = null;
        if (context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId)
        {
            profile = (await preferences.GetDisplayAsync(userId, context.RequestAborted)).Units;
        }

        context.Request.Cookies.TryGetValue(DisplayUnitsRules.CookieName, out var cookie);
        return DisplayUnitsRules.Resolve(null, profile, cookie) == DisplayUnits.Metric ? UnitSystem.Metric : UnitSystem.US;
    }
}
