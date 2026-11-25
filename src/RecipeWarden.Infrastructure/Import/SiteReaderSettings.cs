using RecipeWarden.Infrastructure.Settings;

namespace RecipeWarden.Infrastructure.Import;

/// <summary>
/// FR-IMP-12, BR-117, ADR 0059: the Admin's "site readers for web imports" switch. Off unless turned on (owner,
/// 2026-11-24): turn it on once <c>import-compare</c> shows a gain (D6). The Python features must be on as well (BR-119).
/// </summary>
public sealed class SiteReaderSettings(SettingsStore settings)
{
    public const string SettingKey = "Import.SiteReaders";

    public async Task<bool> EnabledAsync(CancellationToken cancellationToken = default) =>
        string.Equals(await settings.GetAsync(SettingKey, cancellationToken).ConfigureAwait(false), "true", StringComparison.OrdinalIgnoreCase);

    public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        settings.SetAsync(SettingKey, enabled ? "true" : null, cancellationToken); // default off: only "on" is stored
}
