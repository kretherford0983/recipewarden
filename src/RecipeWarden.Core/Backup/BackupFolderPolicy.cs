namespace RecipeWarden.Core.Backup;

/// <summary>What the file system reports for a configured path.</summary>
public enum PathKind
{
    Missing,
    Directory,
    File,
}

/// <summary>One validation problem for a configured folder.</summary>
public sealed record FolderIssue(string Folder, string Reason);

/// <summary>Outcome of validating <c>Backup:AllowedFolders</c>.</summary>
public sealed record FolderValidationResult(
    IReadOnlyList<string> Folders,
    IReadOnlyList<FolderIssue> Errors,
    IReadOnlyList<FolderIssue> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// FR-BKP-08 (PennyWarden BKP-CFG-001 to 003): rules for the folders the app may write backups to.
/// Pure: the caller supplies a probe that reports what exists on disk.
/// </summary>
public static class BackupFolderPolicy
{
    /// <summary>Trees that are never allowed, because the service runs with ProtectHome.</summary>
    public static IReadOnlyList<string> ForbiddenRoots { get; } = ["/home", "/root", "/run/user"];

    public static FolderValidationResult Validate(IEnumerable<string?> configured, Func<string, PathKind> probe)
    {
        ArgumentNullException.ThrowIfNull(configured);
        ArgumentNullException.ThrowIfNull(probe);

        var folders = new List<string>();
        var errors = new List<FolderIssue>();
        var warnings = new List<FolderIssue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in configured)
        {
            var display = raw ?? string.Empty;
            var reason = CheckSyntax(display);
            if (reason is not null)
            {
                errors.Add(new FolderIssue(display, reason));
                continue;
            }

            var folder = Normalize(display);
            if (!seen.Add(folder))
            {
                continue; // duplicate after normalization
            }

            var forbidden = ForbiddenRoots.FirstOrDefault(root => IsSameOrUnder(folder, root));
            if (forbidden is not null)
            {
                errors.Add(new FolderIssue(folder, $"Folders under {forbidden} are not allowed (the service cannot write there)."));
                continue;
            }

            switch (probe(folder))
            {
                case PathKind.File:
                    errors.Add(new FolderIssue(folder, "The path exists but is a file, not a folder."));
                    continue;
                case PathKind.Missing:
                    warnings.Add(new FolderIssue(folder, "The folder does not exist yet (not mounted?). The service will still start."));
                    break;
                case PathKind.Directory:
                    break;
            }

            folders.Add(folder);
        }

        return new FolderValidationResult(folders, errors, warnings);
    }

    /// <summary>Removes trailing slashes and collapses repeated slashes. Assumes <see cref="CheckSyntax"/> passed.</summary>
    public static string Normalize(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var parts = folder.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "/" : "/" + string.Join('/', parts);
    }

    private static string? CheckSyntax(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return "The path is empty.";
        }

        if (folder.Any(char.IsControl))
        {
            return "The path contains control characters.";
        }

        if (!folder.StartsWith('/'))
        {
            return "The path must be absolute (start with /).";
        }

        // Written into a systemd unit file: quotes, backslashes and specifiers (%) would change its meaning.
        if (folder.IndexOfAny(['"', '\\', '%']) >= 0)
        {
            return "The path must not contain \", \\ or %.";
        }

        var segments = folder.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s is "." or ".."))
        {
            return "The path must not contain . or .. segments.";
        }

        if (segments.Length == 0)
        {
            return "The root folder / is not allowed.";
        }

        return null;
    }

    private static bool IsSameOrUnder(string folder, string root) =>
        string.Equals(folder, root, StringComparison.Ordinal) ||
        folder.StartsWith(root + "/", StringComparison.Ordinal);
}
