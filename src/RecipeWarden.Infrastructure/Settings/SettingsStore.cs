using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Health;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Settings;

/// <summary>AppSetting keys (8.5). Values are text; JSON where a value has parts.</summary>
public static class SettingKeys
{
    public const string SiteName = "Site.Name";
    public const string DiskThresholds = "Health.DiskThresholds";
    public const string WebhookUrl = "Health.WebhookUrl";
    public const string WebhookFormat = "Health.WebhookFormat";
    public const string WebhookAllowLan = "Health.WebhookAllowLan";
    public const string LastStatus = "Health.LastStatus";
    public const string TrackingSince = "Health.TrackingSince";
    public const string LastIntegrityAt = "Health.LastIntegrityAt";
    public const string LastIntegrityOk = "Health.LastIntegrityOk";
    public const string LastQuickCheckAt = "Health.LastQuickCheckAt";
    public const string LastFullCheckAt = "Health.LastFullCheckAt";
    public const string BackupPublicKey = "Backup.PublicKey";
    public const string BackupKeyFingerprint = "Backup.KeyFingerprint";
    public const string BackupKeyCreatedAt = "Backup.KeyCreatedAt";
}

/// <summary>Health webhook body shape (FR-OPS-05, ADR 0037).</summary>
public enum WebhookFormat
{
    /// <summary>Plain text body, as ntfy expects.</summary>
    Text,

    /// <summary>{"content": "..."} as Discord expects.</summary>
    Discord,

    /// <summary>{"status", "previous", "message", "checks"} for anything else.</summary>
    Json,
}

/// <summary>/admin/settings values that people change in the app (the server owner's config file holds the rest).</summary>
public sealed record SiteSettings(string SiteName, DiskThresholds Thresholds, string? WebhookUrl, WebhookFormat WebhookFormat, bool WebhookAllowLan)
{
    public const string DefaultSiteName = "RecipeWarden";
    public const int MaxSiteNameLength = 60;
}

/// <summary>Reads and writes AppSetting rows. No cache: a restore replaces them, and reads are one indexed row.</summary>
public sealed class SettingsStore(AppDbContext db)
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        (await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, cancellationToken).ConfigureAwait(false))?.Value;

    public async Task SetAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken).ConfigureAwait(false);
        if (value is null)
        {
            if (row is not null)
            {
                db.AppSettings.Remove(row);
            }
        }
        else if (row is null)
        {
            db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }
        else if (!string.Equals(row.Value, value, StringComparison.Ordinal))
        {
            row.Value = value;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, string>> GetManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var list = keys.ToList();
        return await db.AppSettings.AsNoTracking().Where(s => list.Contains(s.Key)).ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SiteSettings> GetSiteAsync(CancellationToken cancellationToken = default)
    {
        var all = await GetManyAsync([SettingKeys.SiteName, SettingKeys.DiskThresholds, SettingKeys.WebhookUrl, SettingKeys.WebhookFormat, SettingKeys.WebhookAllowLan],
            cancellationToken).ConfigureAwait(false);
        var thresholds = DiskThresholds.Default;
        if (all.TryGetValue(SettingKeys.DiskThresholds, out var json))
        {
            try
            {
                thresholds = JsonSerializer.Deserialize<DiskThresholds>(json) ?? DiskThresholds.Default;
            }
            catch (JsonException)
            {
                thresholds = DiskThresholds.Default;
            }
        }

        return new SiteSettings(
            all.TryGetValue(SettingKeys.SiteName, out var name) && !string.IsNullOrWhiteSpace(name) ? name : SiteSettings.DefaultSiteName,
            thresholds,
            all.GetValueOrDefault(SettingKeys.WebhookUrl),
            all.TryGetValue(SettingKeys.WebhookFormat, out var f) && Enum.TryParse<WebhookFormat>(f, out var format) ? format : WebhookFormat.Text,
            all.TryGetValue(SettingKeys.WebhookAllowLan, out var lan) && lan == "true");
    }

    public async Task SaveSiteAsync(SiteSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await SetAsync(SettingKeys.SiteName, settings.SiteName == SiteSettings.DefaultSiteName ? null : settings.SiteName, cancellationToken).ConfigureAwait(false);
        await SetAsync(SettingKeys.DiskThresholds, settings.Thresholds == DiskThresholds.Default ? null : JsonSerializer.Serialize(settings.Thresholds), cancellationToken)
            .ConfigureAwait(false);
        await SetAsync(SettingKeys.WebhookUrl, string.IsNullOrWhiteSpace(settings.WebhookUrl) ? null : settings.WebhookUrl.Trim(), cancellationToken).ConfigureAwait(false);
        await SetAsync(SettingKeys.WebhookFormat, settings.WebhookFormat.ToString(), cancellationToken).ConfigureAwait(false);
        await SetAsync(SettingKeys.WebhookAllowLan, settings.WebhookAllowLan ? "true" : null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DateTimeOffset?> GetTimeAsync(string key, CancellationToken cancellationToken = default) =>
        DateTimeOffset.TryParse(await GetAsync(key, cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)
            ? t
            : null;

    public Task SetTimeAsync(string key, DateTimeOffset value, CancellationToken cancellationToken = default) =>
        SetAsync(key, value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), cancellationToken);
}
