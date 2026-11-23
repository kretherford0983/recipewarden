using System.Globalization;
using RecipeWarden.Core.Backup;
using RecipeWarden.Infrastructure.Audit;
using RecipeWarden.Infrastructure.Settings;

namespace RecipeWarden.Infrastructure.Backup;

public sealed record BackupKeyStatus(bool HasKey, string? Fingerprint, DateTimeOffset? CreatedAt);

/// <summary>
/// FR-BKP-15, SEC-75, ADR 0039: creates the backup key pair. The public key is kept in AppSetting (so a restore brings
/// the matching key with the data); the private key leaves the server once, inside the recovery kit returned here,
/// and is never stored. Replacing the key needs the Admin to tick that old kits stay needed for older archives.
/// </summary>
public sealed class BackupKeyService(SettingsStore settings, AuditLog audit, TimeProvider clock)
{
    /// <summary>Lower in tests only (PBKDF2 at 600,000 iterations takes a moment by design).</summary>
    internal int KitIterations { get; set; } = BackupCrypto.DefaultKitIterations;

    public async Task<BackupKeyStatus> StatusAsync(CancellationToken cancellationToken = default)
    {
        var all = await settings.GetManyAsync([SettingKeys.BackupPublicKey, SettingKeys.BackupKeyFingerprint, SettingKeys.BackupKeyCreatedAt], cancellationToken)
            .ConfigureAwait(false);
        return new BackupKeyStatus(
            all.ContainsKey(SettingKeys.BackupPublicKey),
            all.GetValueOrDefault(SettingKeys.BackupKeyFingerprint),
            all.TryGetValue(SettingKeys.BackupKeyCreatedAt, out var at) && DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null);
    }

    /// <summary>Returns the new kit text, or an error. The passphrase is used once and never kept (SEC-75).</summary>
    public async Task<(string? Kit, string? Fingerprint, string? Error)> CreateAsync(string? passphrase, string? confirm, bool replaceExisting,
        CancellationToken cancellationToken = default)
    {
        if (RestoreRules.ValidateNewPassphrase(passphrase, confirm) is { } problem)
        {
            return (null, null, problem);
        }

        var status = await StatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.HasKey && !replaceExisting)
        {
            return (null, null, "A key already exists. Tick the box to replace it (keep the old kit for older archives).");
        }

        var now = clock.GetUtcNow();
        var pair = BackupCrypto.CreateKeyPair(passphrase!, now, KitIterations);
        await settings.SetAsync(SettingKeys.BackupPublicKey, pair.PublicKeyPem, cancellationToken).ConfigureAwait(false);
        await settings.SetAsync(SettingKeys.BackupKeyFingerprint, pair.Fingerprint, cancellationToken).ConfigureAwait(false);
        await settings.SetTimeAsync(SettingKeys.BackupKeyCreatedAt, now, cancellationToken).ConfigureAwait(false);
        await audit.RecordAsync(status.HasKey ? "BackupKey.Replace" : "BackupKey.Create", "BackupKey", pair.Fingerprint, null, cancellationToken).ConfigureAwait(false);
        return (pair.RecoveryKit, pair.Fingerprint, null);
    }

    public static string KitFileName(string fingerprint) =>
        "recipewarden-recovery-kit-" + new string(fingerprint.Replace("SHA256:", string.Empty, StringComparison.Ordinal)
            .Where(char.IsAsciiLetterOrDigit).Take(12).ToArray()) + ".txt";
}
