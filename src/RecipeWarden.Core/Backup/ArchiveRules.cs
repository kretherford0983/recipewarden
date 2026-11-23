using System.Globalization;

namespace RecipeWarden.Core.Backup;

/// <summary>One file listed in a backup manifest (BR-60).</summary>
public sealed record ManifestFile(string Path, long Bytes, string Sha256);

/// <summary>
/// BR-60: manifest.json at the root of every archive. <see cref="SchemaVersion"/> is the newest EF migration id
/// applied to the database when it was copied.
/// </summary>
public sealed record BackupManifest(
    string Format,
    int FormatVersion,
    string AppVersion,
    string SchemaVersion,
    DateTimeOffset CreatedAt,
    string Kind,
    IReadOnlyList<ManifestFile> Files)
{
    public const string FormatName = "recipewarden-backup";
    public const int CurrentFormatVersion = 1;
    public const string FileName = "manifest.json";
}

/// <summary>Limits for reading an archive (SEC-54). Defaults suit a household database with photos.</summary>
public sealed record ArchiveLimits(long MaxTotalBytes, long MaxFileBytes, int MaxEntries)
{
    public static ArchiveLimits Default { get; } = new(20L * 1024 * 1024 * 1024, 8L * 1024 * 1024 * 1024, 200_000);
}

/// <summary>
/// SEC-54, BR-62, BR-65: what an archive may contain, checked before anything is extracted, and whether its version
/// can be restored here. Archive layout (ADR 0040): <c>manifest.json</c>, <c>db/recipewarden.db</c>, <c>media/…</c>,
/// <c>keys/…</c> (Data Protection key ring, ADR 0038) and <c>config/…</c> (kept for reference, never applied).
/// </summary>
public static class ArchiveRules
{
    public const string DatabaseEntry = "db/recipewarden.db";
    public static IReadOnlyList<string> AllowedRoots { get; } = ["media/", "keys/", "config/"];
    public const int MaxEntryNameLength = 512;

    /// <summary>Null when the entry name is safe; otherwise the reason (zip-slip and friends).</summary>
    public static string? CheckEntryName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "an entry has no name";
        }

        if (name.Length > MaxEntryNameLength)
        {
            return "an entry name is too long";
        }

        if (name.StartsWith('/') || name.Contains('\\', StringComparison.Ordinal) || (name.Length > 1 && name[1] == ':'))
        {
            return $"\"{Shorten(name)}\" is an absolute path";
        }

        if (name.Any(char.IsControl))
        {
            return "an entry name contains control characters";
        }

        var segments = name.TrimEnd('/').Split('/');
        if (segments.Any(s => s is ".." or "." or ""))
        {
            return $"\"{Shorten(name)}\" contains \"..\" or an empty path segment";
        }

        if (name is BackupManifest.FileName or DatabaseEntry || AllowedRoots.Any(r => name.StartsWith(r, StringComparison.Ordinal) && name.Length > r.Length))
        {
            return null;
        }

        return name is "db/" or "db" || AllowedRoots.Any(r => name == r || name + "/" == r) ? null : $"\"{Shorten(name)}\" is not part of a RecipeWarden backup";
    }

    /// <summary>
    /// BR-62: the manifest must name this format, list the database, and list every file with a SHA-256; every
    /// listed path must itself be a safe entry name.
    /// </summary>
    public static string? CheckManifest(BackupManifest? manifest)
    {
        if (manifest is null)
        {
            return "The archive has no manifest.json; it is not a RecipeWarden backup.";
        }

        if (!string.Equals(manifest.Format, BackupManifest.FormatName, StringComparison.Ordinal))
        {
            return "The manifest is not a RecipeWarden backup manifest.";
        }

        if (manifest.FormatVersion > BackupManifest.CurrentFormatVersion)
        {
            return InstallNewer(manifest.AppVersion);
        }

        if (manifest.FormatVersion < 1 || manifest.Files is null || string.IsNullOrWhiteSpace(manifest.SchemaVersion))
        {
            return "The manifest is incomplete.";
        }

        if (!manifest.Files.Any(f => f.Path == DatabaseEntry))
        {
            return "The manifest does not list the database.";
        }

        foreach (var f in manifest.Files)
        {
            var problem = f.Path == BackupManifest.FileName ? f.Path : CheckEntryName(f.Path);
            if (problem is not null)
            {
                return "The manifest lists an unsafe path: " + problem;
            }

            if (f.Bytes < 0 || f.Sha256 is null || f.Sha256.Length != 64 || !f.Sha256.All(char.IsAsciiHexDigit))
            {
                return $"The manifest entry for {f.Path} has no valid SHA-256.";
            }
        }

        return manifest.Files.Select(f => f.Path).Distinct(StringComparer.Ordinal).Count() == manifest.Files.Count
            ? null
            : "The manifest lists a file twice.";
    }

    /// <summary>
    /// BR-65: an archive from a newer app, or with a database schema this build does not know, is refused with
    /// "Install version X or later, then restore". Older archives are migrated forward after the restore.
    /// </summary>
    /// <param name="knownMigrations">Every migration id this build contains.</param>
    public static string? CheckVersion(BackupManifest manifest, string currentAppVersion, IReadOnlyCollection<string> knownMigrations)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(knownMigrations);
        if (CompareVersions(manifest.AppVersion, currentAppVersion) > 0 || !knownMigrations.Contains(manifest.SchemaVersion))
        {
            return InstallNewer(manifest.AppVersion);
        }

        return null;
    }

    public static string InstallNewer(string? version) =>
        $"This backup was made by RecipeWarden {(string.IsNullOrWhiteSpace(version) ? "(a newer version)" : version)}. Install version {CoreVersion(version)} or later, then restore.";

    /// <summary>Semantic-version order: 0.7.0-dev.3 &lt; 0.7.0-dev.12 &lt; 0.7.0 &lt; 0.7.1. Unparsable versions sort first.</summary>
    public static int CompareVersions(string? a, string? b)
    {
        var (ca, pa) = Parse(a);
        var (cb, pb) = Parse(b);
        for (var i = 0; i < 3; i++)
        {
            var c = ca[i].CompareTo(cb[i]);
            if (c != 0)
            {
                return c;
            }
        }

        if (pa is null || pb is null)
        {
            return pa is null ? (pb is null ? 0 : 1) : -1; // a release is newer than its pre-releases
        }

        var (la, na) = SplitPre(pa);
        var (lb, nb) = SplitPre(pb);
        var label = string.CompareOrdinal(la, lb);
        return label != 0 ? label : na.CompareTo(nb);
    }

    private static string CoreVersion(string? version) =>
        string.Join('.', Parse(version).Core.Select(n => n.ToString(CultureInfo.InvariantCulture)));

    private static (int[] Core, string? Pre) Parse(string? version)
    {
        var v = (version ?? string.Empty).Trim().TrimStart('v');
        var plus = v.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            v = v[..plus];
        }

        var dash = v.IndexOf('-', StringComparison.Ordinal);
        var pre = dash >= 0 ? v[(dash + 1)..] : null;
        var parts = (dash >= 0 ? v[..dash] : v).Split('.');
        var core = new int[3];
        for (var i = 0; i < 3 && i < parts.Length; i++)
        {
            _ = int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out core[i]);
        }

        return (core, string.IsNullOrEmpty(pre) ? null : pre);
    }

    private static (string Label, int Number) SplitPre(string pre)
    {
        var dot = pre.LastIndexOf('.');
        return dot > 0 && int.TryParse(pre[(dot + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? (pre[..dot], n)
            : (pre, 0);
    }

    private static string Shorten(string name) => name.Length <= 60 ? name : name[..57] + "...";
}
