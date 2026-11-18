namespace RecipeWarden.Core.Preferences;

/// <summary>FR-UI-01, UX-03: the three supported themes.</summary>
public enum Theme
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>FR-UI-01: parsing and serialisation rules for the theme preference.</summary>
public static class ThemeRules
{
    /// <summary>Cookie that carries the theme for anonymous visitors (set client-side, SEC-31).</summary>
    public const string CookieName = "rw-theme";

    /// <summary>One year; the cookie is a convenience, not a security token.</summary>
    public static readonly TimeSpan CookieLifetime = TimeSpan.FromDays(365);

    /// <summary>Parses a stored or submitted value. Anything unknown falls back to <see cref="Theme.System"/>.</summary>
    public static Theme Parse(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "LIGHT" => Theme.Light,
        "DARK" => Theme.Dark,
        _ => Theme.System,
    };

    /// <summary>Strict parse for input from a form: unknown values are rejected rather than defaulted.</summary>
    public static bool TryParseStrict(string? value, out Theme theme)
    {
        switch (value?.Trim().ToUpperInvariant())
        {
            case "SYSTEM": theme = Theme.System; return true;
            case "LIGHT": theme = Theme.Light; return true;
            case "DARK": theme = Theme.Dark; return true;
            default: theme = Theme.System; return false;
        }
    }

    /// <summary>The value written to the cookie and to the <c>data-theme</c> attribute.</summary>
    public static string ToValue(Theme theme) => theme switch
    {
        Theme.Light => "light",
        Theme.Dark => "dark",
        _ => "system",
    };

    /// <summary>
    /// Effective theme for a request: a signed-in user's saved profile wins, then the cookie, then System.
    /// </summary>
    public static Theme Resolve(Theme? profileTheme, string? cookieValue) =>
        profileTheme ?? Parse(cookieValue);
}
