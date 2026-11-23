namespace RecipeWarden.Core.Backup;

/// <summary>How writing to a backup folder failed (FR-BKP-11), as the caller classifies the OS error.</summary>
public enum FolderProblem
{
    None,
    MarkerMissing,
    ReadOnly,
    PermissionDenied,
    NotFound,
    NoSpace,
    Other,
}

/// <summary>
/// FR-BKP-10 to 13, SEC-74 (PennyWarden BKP-APP-001 to 004, 009): destinations come from Backup:AllowedFolders only,
/// the mount marker, atomic names, and the hints shown for write failures.
/// </summary>
public static class BackupDestinationRules
{
    /// <summary>FR-BKP-12: created by the server owner in every allowed folder.</summary>
    public const string MarkerFileName = ".recipewarden-backup-target";

    /// <summary>FR-BKP-11: the probe file the Test button writes and deletes.</summary>
    public const string ProbePrefix = ".recipewarden-write-test-";

    public const string NotMounted = "The backup folder is not mounted or is the wrong location";

    /// <summary>
    /// SEC-74: the saved destination is the normalized matching entry from the allowed list, never the request text.
    /// Returns null when <paramref name="requested"/> does not match an allowed folder exactly.
    /// </summary>
    public static string? Resolve(string? requested, IEnumerable<string> allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        if (string.IsNullOrWhiteSpace(requested) || !requested.StartsWith('/') || requested.Any(char.IsControl))
        {
            return null;
        }

        var wanted = BackupFolderPolicy.Normalize(requested);
        return allowed.FirstOrDefault(a => string.Equals(a, wanted, StringComparison.Ordinal));
    }

    /// <summary>FR-BKP-13: archives are written as ".&lt;name&gt;.partial" and renamed when complete.</summary>
    public static string PartialName(string fileName) => "." + fileName + ".partial";

    /// <summary>"backup-YYYYMMDD-HHMMSS.tar.gz", with ".rwenc" when encrypted (section 9.6, ADR 0039).</summary>
    public static string ArchiveName(DateTimeOffset at, bool encrypted) =>
        "backup-" + at.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".tar.gz" + (encrypted ? ".rwenc" : string.Empty);

    /// <summary>FR-BKP-11: the message and hint for a failed write or Test.</summary>
    public static string Describe(FolderProblem problem, string? osMessage = null) => problem switch
    {
        FolderProblem.None => "The folder is writable.",
        FolderProblem.MarkerMissing => NotMounted + $" (the marker file {MarkerFileName} is missing). Nothing was written.",
        FolderProblem.ReadOnly => Os(osMessage, "Read-only file system") + " — run \"sudo recipewarden apply-config\", or restart the service if the share was mounted after it started.",
        FolderProblem.PermissionDenied => Os(osMessage, "Permission denied") + " — the folder (or the share's mount options) must be owned by the recipewarden user.",
        FolderProblem.NotFound => Os(osMessage, "No such file or directory") + " — the folder does not exist; check that the share is mounted.",
        FolderProblem.NoSpace => Os(osMessage, "No space left on device") + " — free space in that folder or tighten retention.",
        _ => Os(osMessage, "The folder could not be written"),
    };

    private static string Os(string? message, string fallback) => string.IsNullOrWhiteSpace(message) ? fallback : message.Trim().TrimEnd('.');
}

/// <summary>FR-BKP-15, SEC-75, W7: rules for the recovery kit passphrase and the restore confirmation.</summary>
public static class RestoreRules
{
    public const int MinPassphraseLength = 12;
    public const int MaxPassphraseLength = 1024;

    /// <summary>W7: the word typed to confirm a restore.</summary>
    public const string ConfirmationWord = "RESTORE";

    public static string? ValidateNewPassphrase(string? passphrase, string? confirm)
    {
        if (string.IsNullOrEmpty(passphrase) || passphrase.Length < MinPassphraseLength)
        {
            return $"Use a passphrase of at least {MinPassphraseLength} characters (a few unrelated words work well).";
        }

        if (passphrase.Length > MaxPassphraseLength)
        {
            return $"The passphrase can be at most {MaxPassphraseLength} characters.";
        }

        return string.Equals(passphrase, confirm, StringComparison.Ordinal) ? null : "The two passphrases do not match.";
    }

    public static bool IsConfirmed(string? typed) => string.Equals(typed?.Trim(), ConfirmationWord, StringComparison.Ordinal);
}
