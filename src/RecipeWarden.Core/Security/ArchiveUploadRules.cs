namespace RecipeWarden.Core.Security;

/// <summary>The two large uploads Admins make (M7): a backup archive to restore, and a USDA dataset zip.</summary>
public enum ArchiveUploadKind
{
    /// <summary>.tar.gz (gzip magic) or .rwenc (RecipeWarden encrypted archive, ADR 0039).</summary>
    Backup,

    /// <summary>.zip (PK magic), FoodData Central CSV download (FR-NUT-05).</summary>
    NutrientDataset,
}

/// <summary>
/// SEC-50, SEC-54 (ADR 0040): extension and first bytes must agree, and the size is capped. The content itself is
/// checked later by the reader (restore validation, dataset importer) before anything is applied.
/// </summary>
public static class ArchiveUploadRules
{
    public const string WrongBackupType = "Choose a RecipeWarden backup (.tar.gz or .tar.gz.rwenc).";
    public const string WrongDatasetType = "Choose a FoodData Central CSV download (.zip).";
    public static ReadOnlySpan<byte> EncryptedMagic => "RWENC\u0001"u8;

    public static string? Check(ArchiveUploadKind kind, string? fileName, long length, ReadOnlySpan<byte> head, long maxBytes)
    {
        var name = (fileName ?? string.Empty).ToLowerInvariant();
        if (length > maxBytes)
        {
            return $"The file is larger than {maxBytes / (1024 * 1024)} MB. Copy it to the server and use the command line instead.";
        }

        if (kind == ArchiveUploadKind.Backup)
        {
            var gzip = (name.EndsWith(".tar.gz", StringComparison.Ordinal) || name.EndsWith(".tgz", StringComparison.Ordinal))
                && head.Length >= 2 && head[0] == 0x1F && head[1] == 0x8B;
            var encrypted = name.EndsWith(".rwenc", StringComparison.Ordinal) && head.StartsWith(EncryptedMagic);
            return gzip || encrypted ? null : WrongBackupType;
        }

        return name.EndsWith(".zip", StringComparison.Ordinal) && head.Length >= 4 && head[0] == (byte)'P' && head[1] == (byte)'K' && head[2] == 3 && head[3] == 4
            ? null
            : WrongDatasetType;
    }
}
