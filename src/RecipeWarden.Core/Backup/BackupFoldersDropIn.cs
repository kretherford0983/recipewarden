using System.Text;

namespace RecipeWarden.Core.Backup;

/// <summary>
/// FR-BKP-09 (BKP-SVC-001), SEC-70: content of the systemd drop-in that opens the allowed backup folders
/// in the service sandbox.
/// </summary>
public static class BackupFoldersDropIn
{
    public const string DirectoryPath = "/etc/systemd/system/recipewarden.service.d";
    public const string FileName = "backup-folders.conf";
    public const string FilePath = DirectoryPath + "/" + FileName;

    /// <summary>The operator-owned drop-in that apply-config must never touch.</summary>
    public const string OverrideFilePath = DirectoryPath + "/override.conf";

    public const string Header = "# Written by \"recipewarden apply-config\" from Backup:AllowedFolders - don't edit";

    /// <summary>
    /// Renders the drop-in, or returns null when there are no folders (the file must then be removed).
    /// Folders must already be validated and normalized by <see cref="BackupFolderPolicy"/>.
    /// </summary>
    public static string? Render(IReadOnlyList<string> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        if (folders.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.Append(Header).Append('\n');
        sb.Append("[Service]\n");
        foreach (var folder in folders)
        {
            // Leading "-" lets the service start when a share is not mounted.
            sb.Append("ReadWritePaths=\"-").Append(folder).Append("\"\n");
        }

        return sb.ToString();
    }

    /// <summary>Reads the folders back out of an existing drop-in (for "what changed" output).</summary>
    public static IReadOnlyList<string> Parse(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return [];
        }

        const string prefix = "ReadWritePaths=\"-";
        return content
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith(prefix, StringComparison.Ordinal) && l.EndsWith('"'))
            .Select(l => l[prefix.Length..^1])
            .ToList();
    }
}
