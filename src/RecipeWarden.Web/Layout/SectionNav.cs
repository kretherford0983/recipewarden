namespace RecipeWarden.Web.Layout;

/// <summary>One link in a section's tab bar (ADR 0047): where it goes, its icon and name, and the paths it covers.</summary>
public sealed record SectionLink(string Href, string Icon, string Label, params string[] Covers);

/// <summary>
/// ADR 0047, UX-13: the tab bars under the page title for the Manage and Admin areas (PennyWarden's page tabs, as
/// links: each page keeps its own address). The layout shows them on every page of the area.
/// </summary>
public static class SectionNav
{
    public static readonly IReadOnlyList<SectionLink> Manage =
    [
        new("/manage", "manage", "Overview"),
        new("/manage/import", "import", "Import", "/manage/import"),
        new("/manage/collections", "collections", "Collections", "/manage/collections"),
        new("/manage/tags", "tag", "Tags", "/manage/tags"),
        new("/manage/kitchen", "kitchen", "My Kitchen", "/manage/kitchen"),
        new("/manage/conversions", "scale", "Conversions", "/manage/conversions"),
        new("/manage/nutrition", "nutrition", "Nutrition", "/manage/nutrition"),
        new("/manage/trash", "trash", "Trash", "/manage/trash"),
    ];

    public static readonly IReadOnlyList<SectionLink> Admin =
    [
        new("/admin", "admin", "Overview"),
        new("/admin/health", "health", "Health", "/admin/health"),
        new("/admin/backups", "backup", "Backups", "/admin/backups"),
        new("/admin/users", "users", "Users", "/admin/users"),
        new("/admin/settings", "settings", "Settings", "/admin/settings"),
        new("/admin/audit", "audit", "Audit log", "/admin/audit"),
        new("/admin/nutrition-data", "nutrition", "Nutrition data", "/admin/nutrition-data"),
        new("/admin/ai", "ai", "AI parser", "/admin/ai"),
    ];

    /// <summary>The link for the page at <paramref name="path"/>: an exact match, else the longest covering prefix.</summary>
    public static bool IsCurrent(SectionLink link, IReadOnlyList<SectionLink> links, string path)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(path);
        var trimmed = path.TrimEnd('/');
        if (trimmed.Length == 0)
        {
            trimmed = "/";
        }

        var best = links
            .Where(l => string.Equals(l.Href, trimmed, StringComparison.OrdinalIgnoreCase)
                || l.Covers.Any(c => trimmed.StartsWith(c + "/", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(trimmed, c, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(l => l.Href.Length)
            .FirstOrDefault();
        return ReferenceEquals(best, link);
    }
}
