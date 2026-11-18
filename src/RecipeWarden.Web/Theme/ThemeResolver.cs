using System.Security.Claims;
using RecipeWarden.Core.Preferences;
using RecipeWarden.Infrastructure.Identity;

namespace RecipeWarden.Web.Theme;

/// <summary>FR-UI-01, UX-03: the theme rendered into &lt;html data-theme&gt; on the server, so the first paint is right.</summary>
public sealed class ThemeResolver(UserPreferenceStore preferences)
{
    public async Task<string> GetValueAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Core.Preferences.Theme? profile = null;
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is not null)
            {
                profile = await preferences.GetThemeAsync(userId, context.RequestAborted);
            }
        }

        context.Request.Cookies.TryGetValue(ThemeRules.CookieName, out var cookie);
        return ThemeRules.ToValue(ThemeRules.Resolve(profile, cookie));
    }

    /// <summary>Copies a profile theme into the cookie at sign-in so anonymous pages after sign-out match.</summary>
    public static void WriteCookie(HttpResponse response, Core.Preferences.Theme theme)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Cookies.Append(ThemeRules.CookieName, ThemeRules.ToValue(theme), new CookieOptions
        {
            Path = "/",
            MaxAge = ThemeRules.CookieLifetime,
            SameSite = SameSiteMode.Lax,
            Secure = true,
            HttpOnly = false, // read by theme.js; carries no secret
            IsEssential = true,
        });
    }
}
