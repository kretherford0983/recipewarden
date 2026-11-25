using System.Text.Json.Serialization;

namespace RecipeWarden.Core.Presentation;

/// <summary>One icon in the web app manifest.</summary>
public sealed record ManifestIcon(
    [property: JsonPropertyName("src")] string Src,
    [property: JsonPropertyName("sizes")] string Sizes,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("purpose")] string Purpose);

/// <summary>A launcher shortcut (right-click on the installed app's icon).</summary>
public sealed record ManifestShortcut(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("url")] string Url);

/// <summary>
/// UX-15, ADR 0060: the web app manifest that makes the site installable as an app (Chrome and Edge "Install",
/// Android "Add to home screen"). Named from the site name setting; no service worker is needed to install.
/// </summary>
public sealed record AppManifest(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("short_name")] string ShortName,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("start_url")] string StartUrl,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("display")] string Display,
    [property: JsonPropertyName("background_color")] string BackgroundColor,
    [property: JsonPropertyName("theme_color")] string ThemeColor,
    [property: JsonPropertyName("icons")] IReadOnlyList<ManifestIcon> Icons,
    [property: JsonPropertyName("shortcuts")] IReadOnlyList<ManifestShortcut> Shortcuts)
{
    /// <summary>The brand colour (the header logo's terracotta), used for the title bar of the installed app.</summary>
    public const string ThemeColorLight = "#a33f19";

    /// <summary>The dark theme's page colour, for the browser bar in dark mode.</summary>
    public const string ThemeColorDark = "#171513";

    /// <summary>The light theme's page colour, shown while the installed app starts.</summary>
    public const string Background = "#f6f3ee";

    /// <summary>Launcher names are cut at about 12 characters; a longer site name uses its first word.</summary>
    public const int MaxShortName = 12;

    public static AppManifest For(string? siteName)
    {
        var name = string.IsNullOrWhiteSpace(siteName) ? "RecipeWarden" : siteName.Trim();
        var shortName = name.Length <= MaxShortName ? name : name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        if (shortName.Length > MaxShortName)
        {
            shortName = shortName[..MaxShortName];
        }

        return new AppManifest("/", name, shortName, "Your household's recipes, scaled to your kitchen.", "/", "/", "standalone",
            Background, ThemeColorLight,
            [
                new("/icons/icon-192.png", "192x192", "image/png", "any"),
                new("/icons/icon-512.png", "512x512", "image/png", "any"),
                new("/icons/maskable-192.png", "192x192", "image/png", "maskable"),
                new("/icons/maskable-512.png", "512x512", "image/png", "maskable"),
            ],
            [new("Search recipes", "/search"), new("Shopping lists", "/shopping"), new("Browse", "/recipes")]);
    }
}
