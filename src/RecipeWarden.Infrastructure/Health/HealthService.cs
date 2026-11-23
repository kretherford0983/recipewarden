using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RecipeWarden.Core.Health;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Audit;
using RecipeWarden.Infrastructure.Backup;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Import;
using RecipeWarden.Infrastructure.Settings;

namespace RecipeWarden.Infrastructure.Health;

/// <summary>Measures a volume on its real mount point (statvfs). A seam so tests can simulate a full disk (AC-99).</summary>
public interface IVolumeProbe
{
    (long Total, long Free) Measure(string path);
}

/// <summary>DriveInfo on Linux calls statvfs on the given path, so any folder reports the volume it lives on.</summary>
public sealed class DriveInfoVolumeProbe : IVolumeProbe
{
    public (long Total, long Free) Measure(string path)
    {
        try
        {
            var existing = path;
            while (!Directory.Exists(existing) && Path.GetDirectoryName(existing) is { } parent && parent != existing)
            {
                existing = parent;
            }

            var drive = new DriveInfo(existing);
            return (drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return (0, 0);
        }
    }
}

/// <summary>Health settings from the config file ("Health" section).</summary>
public sealed class HealthOptions
{
    public const string SectionName = "Health";

    /// <summary>The rolling log files' folder (section 9.5), counted as "logs" on the health page.</summary>
    public string LogDirectory { get; set; } = "/var/log/recipewarden";

    /// <summary>How often the monitor evaluates (minutes).</summary>
    public int IntervalMinutes { get; set; } = 15;
}

/// <summary>Database figures for FR-OPS-02.</summary>
public sealed record DatabaseStats(long FileBytes, long WalBytes, long PageSize, long PageCount, long FreePages, string SchemaVersion,
    IReadOnlyDictionary<string, int> Counts);

/// <summary>Everything /admin/health shows (FR-OPS-02, AC-98).</summary>
public sealed record HealthReport(
    HealthStatus Status,
    IReadOnlyList<HealthCheck> Checks,
    IReadOnlyList<VolumeUsage> Volumes,
    SpaceByCategory Space,
    DatabaseStats Database,
    DateTimeOffset? LastIntegrityAt,
    bool? LastIntegrityOk,
    DateTimeOffset? LastBackupAt,
    decimal? DaysUntilFull,
    string? Recommendation,
    IReadOnlyList<StorageSnapshot> History,
    DateTimeOffset At);

/// <summary>The latest overall status, for the Admin banner and /health/ready (cheap to read on every request).</summary>
public sealed class HealthState
{
    private volatile Snapshot current = new(HealthStatus.OK, null, null, DateTimeOffset.MinValue);

    public HealthStatus Status => current.Status;

    /// <summary>The banner text for Admins when the status is not OK (FR-OPS-03).</summary>
    public string? Banner => current.Banner;

    /// <summary>FR-BKP-14: the banner for a failed backup run.</summary>
    public string? BackupBanner => current.BackupBanner;

    public DateTimeOffset UpdatedAt => current.UpdatedAt;

    public void Set(HealthStatus status, string? banner, string? backupBanner, DateTimeOffset at) => current = new Snapshot(status, banner, backupBanner, at);

    private sealed record Snapshot(HealthStatus Status, string? Banner, string? BackupBanner, DateTimeOffset UpdatedAt);
}

/// <summary>
/// FR-OPS-02 to 05, BR-80 to 85, section 9.6: measures storage and the database, evaluates the rules in Core, keeps the
/// daily snapshot (400 days) and check results (90 days), runs integrity checks and VACUUM, and posts to the optional
/// webhook when the status changes (through SafeFetcher, ADR 0041).
/// </summary>
public sealed partial class HealthService(
    AppDbContext db,
    DataOptions data,
    BackupFolders folders,
    SettingsStore settings,
    IVolumeProbe volumes,
    HealthState state,
    SafeFetcher fetcher,
    HealthOptions options,
    TimeProvider clock,
    ILogger<HealthService> logger)
{
    public static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;
    public static readonly TimeSpan SnapshotRetention = TimeSpan.FromDays(400);
    public static readonly TimeSpan ResultRetention = TimeSpan.FromDays(90);

    public string LogDirectory => options.LogDirectory;

    public async Task<HealthReport> EvaluateAsync(bool store, CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        var now = clock.GetUtcNow();
        var site = await settings.GetSiteAsync(cancellationToken).ConfigureAwait(false);
        var stats = await DatabaseStatsAsync(cancellationToken).ConfigureAwait(false);
        var space = MeasureSpace(stats);

        var volumeList = new List<VolumeUsage>();
        var (dt, df) = volumes.Measure(data.Directory);
        volumeList.Add(new VolumeUsage("data", data.Directory, dt, df));
        var unreachable = false;
        var schedules = await db.BackupSchedules.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var folder in schedules.Where(s => s.IsEnabled).Select(s => s.DestinationFolder).Distinct().Where(f => f != folders.Local))
        {
            var check = folders.CheckMarker(folder);
            unreachable |= !check.Ok;
            var (bt, bf) = volumes.Measure(folder);
            volumeList.Add(new VolumeUsage("backups " + folder, folder, check.Ok ? bt : 0, check.Ok ? bf : 0));
        }

        var checks = new List<HealthCheck>();
        foreach (var v in volumeList)
        {
            checks.Add(HealthRules.Volume(v, site.Thresholds));
        }

        var history = await db.StorageSnapshots.AsNoTracking().Where(s => s.TakenAt >= now.AddDays(-90)).OrderBy(s => s.TakenAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var days = HealthRules.DaysUntilFull(history.Select(h => new UsagePoint(h.TakenAt, h.DataVolumeTotal - h.DataVolumeFree)), df);
        checks.Add(HealthRules.Projection("data", days));

        var integrityAt = await settings.GetTimeAsync(SettingKeys.LastIntegrityAt, cancellationToken).ConfigureAwait(false);
        var integrityText = await settings.GetAsync(SettingKeys.LastIntegrityOk, cancellationToken).ConfigureAwait(false);
        bool? integrityOk = integrityText is null ? null : integrityText == "true";
        checks.Add(HealthRules.Database(integrityOk, stats.PageCount, stats.FreePages, stats.WalBytes));

        var lastBackup = await db.BackupRecords.AsNoTracking()
            .Where(r => r.Status == BackupStatus.Succeeded && r.DeletedAt == null)
            .OrderByDescending(r => r.Id).Select(r => r.CompletedAt).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        checks.Add(HealthRules.Backups(lastBackup, BackupScheduleService.ShortestInterval(schedules, now), unreachable, now));

        var status = HealthRules.Overall(checks);
        var diskStatus = checks.Where(c => c.Name.StartsWith("Disk", StringComparison.Ordinal) || c.Name.StartsWith("Capacity", StringComparison.Ordinal))
            .Select(c => c.Status).DefaultIfEmpty(HealthStatus.OK).Max();
        var recommendation = HealthRules.Recommendation(diskStatus, space);

        var backupBanner = await FailedBackupBannerAsync(cancellationToken).ConfigureAwait(false);
        state.Set(status, Banner(status, checks, recommendation), backupBanner, now);

        if (store)
        {
            using var quiet = AuditScope.Suppress(); // bookkeeping, not a person's write
            foreach (var c in checks)
            {
                db.HealthCheckResults.Add(new HealthCheckResult
                {
                    CheckName = Core.Audit.AuditRules.Truncate(c.Name, 128),
                    RanAt = now,
                    Status = c.Status,
                    Message = Core.Audit.AuditRules.Truncate(c.Message, 1000),
                    DurationMs = (int)watch.ElapsedMilliseconds,
                });
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await NotifyOnChangeAsync(status, checks, site, cancellationToken).ConfigureAwait(false);
        }

        return new HealthReport(status, checks, volumeList, space, stats, integrityAt, integrityOk, lastBackup, days, recommendation, history, now);
    }

    /// <summary>FR-OPS-03: one snapshot per day (kept 400 days); older check results pruned after 90 days.</summary>
    public async Task<bool> TakeDailySnapshotAsync(CancellationToken cancellationToken = default)
    {
        using var quiet = AuditScope.Suppress();
        var now = clock.GetUtcNow();
        var today = now.UtcDateTime.Date;
        var last = await db.StorageSnapshots.AsNoTracking().OrderByDescending(s => s.Id).Select(s => (DateTimeOffset?)s.TakenAt)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (last is { } l && l.UtcDateTime.Date == today)
        {
            return false;
        }

        var stats = await DatabaseStatsAsync(cancellationToken).ConfigureAwait(false);
        var space = MeasureSpace(stats);
        var (dt, df) = volumes.Measure(data.Directory);
        var (bt, bf) = volumes.Measure(folders.Local);
        db.StorageSnapshots.Add(new StorageSnapshot
        {
            TakenAt = now,
            DbBytes = stats.FileBytes,
            WalBytes = stats.WalBytes,
            FreePages = stats.FreePages,
            MediaBytes = space.Media,
            ImportBytes = space.Imports,
            BackupBytes = space.Backups,
            LogBytes = space.Logs,
            DataVolumeTotal = dt,
            DataVolumeFree = df,
            BackupVolumeTotal = bt,
            BackupVolumeFree = bf,
        });

        var oldSnapshots = now - SnapshotRetention;
        var oldResults = now - ResultRetention;
        await db.StorageSnapshots.Where(s => s.TakenAt < oldSnapshots).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.HealthCheckResults.Where(r => r.RanAt < oldResults).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>BR-83: quick_check daily, full integrity_check weekly (or on demand). Returns the result text.</summary>
    public async Task<string> RunIntegrityCheckAsync(bool full, CancellationToken cancellationToken = default)
    {
        using var quiet = AuditScope.Suppress();
        string result;
        var connection = new SqliteConnection(data.ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            result = BackupService.Pragma(connection, full ? SqlitePragma.IntegrityCheck : SqlitePragma.QuickCheck) ?? "no result";
        }

        var now = clock.GetUtcNow();
        await settings.SetTimeAsync(SettingKeys.LastIntegrityAt, now, cancellationToken).ConfigureAwait(false);
        await settings.SetAsync(SettingKeys.LastIntegrityOk, result == "ok" ? "true" : "false", cancellationToken).ConfigureAwait(false);
        await settings.SetTimeAsync(SettingKeys.LastQuickCheckAt, now, cancellationToken).ConfigureAwait(false); // a full check includes the quick one
        if (full)
        {
            await settings.SetTimeAsync(SettingKeys.LastFullCheckAt, now, cancellationToken).ConfigureAwait(false);
        }
        return result;
    }

    /// <summary>FR-OPS-04: VACUUM (compact) and fold the WAL into the file. Returns bytes reclaimed.</summary>
    public async Task<long> CompactAsync(CancellationToken cancellationToken = default)
    {
        var before = FileSize(data.DatabasePath) + FileSize(data.DatabasePath + "-wal");
        var connection = new SqliteConnection(data.ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            BackupService.Pragma(connection, SqlitePragma.Vacuum);
            BackupService.Pragma(connection, SqlitePragma.Checkpoint);
        }

        return Math.Max(0, before - FileSize(data.DatabasePath) - FileSize(data.DatabasePath + "-wal"));
    }

    /// <summary>Whether a daily quick check or weekly full check is due (BR-83).</summary>
    public async Task<(bool Quick, bool Full)> ChecksDueAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var quick = await settings.GetTimeAsync(SettingKeys.LastQuickCheckAt, cancellationToken).ConfigureAwait(false);
        var full = await settings.GetTimeAsync(SettingKeys.LastFullCheckAt, cancellationToken).ConfigureAwait(false);
        var fullDue = full is null || now - full.Value >= TimeSpan.FromDays(7);
        return (!fullDue && (quick is null || now - quick.Value >= TimeSpan.FromDays(1)), fullDue);
    }

    public async Task<DatabaseStats> DatabaseStatsAsync(CancellationToken cancellationToken = default)
    {
        long pageSize, pageCount, freePages;
        var connection = new SqliteConnection(data.ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            pageSize = Long(BackupService.Pragma(connection, SqlitePragma.PageSize));
            pageCount = Long(BackupService.Pragma(connection, SqlitePragma.PageCount));
            freePages = Long(BackupService.Pragma(connection, SqlitePragma.FreelistCount));
        }

        var schema = (await db.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).LastOrDefault() ?? "none";
        var counts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Recipes"] = await db.Recipes.CountAsync(cancellationToken).ConfigureAwait(false),
            ["Images"] = await db.Images.CountAsync(cancellationToken).ConfigureAwait(false),
            ["Tags"] = await db.Tags.CountAsync(cancellationToken).ConfigureAwait(false),
            ["Collections"] = await db.Collections.CountAsync(cancellationToken).ConfigureAwait(false),
            ["Users"] = await db.Users.CountAsync(cancellationToken).ConfigureAwait(false),
            ["Shopping lists"] = await db.ShoppingLists.CountAsync(cancellationToken).ConfigureAwait(false),
            ["Audit entries"] = await db.AuditEntries.CountAsync(cancellationToken).ConfigureAwait(false),
        };
        return new DatabaseStats(FileSize(data.DatabasePath), FileSize(data.DatabasePath + "-wal"), pageSize, pageCount, freePages, schema, counts);
    }

    public SpaceByCategory MeasureSpace(DatabaseStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        return new SpaceByCategory(stats.FileBytes + stats.WalBytes + FileSize(data.DatabasePath + "-shm"), DirectorySize(data.MediaDirectory),
            DirectorySize(data.ImportsDirectory), DirectorySize(folders.Local), DirectorySize(LogDirectory));
    }

    private async Task<string?> FailedBackupBannerAsync(CancellationToken ct)
    {
        var schedules = await db.BackupSchedules.AsNoTracking().Where(s => s.IsEnabled).ToListAsync(ct).ConfigureAwait(false);
        foreach (var s in schedules)
        {
            var last = await db.BackupRecords.AsNoTracking().Where(r => r.ScheduleId == s.Id && r.Status != BackupStatus.Running)
                .OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (last?.Status == BackupStatus.Failed)
            {
                var retry = s.NextRetryAt is { } r ? " Next try " + r.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "." : string.Empty;
                return $"Backup \"{s.Name}\" failed: {last.Error}{retry}";
            }
        }

        return null;
    }

    private static string? Banner(HealthStatus status, List<HealthCheck> checks, string? recommendation)
    {
        if (status == HealthStatus.OK)
        {
            return null;
        }

        var worst = checks.Where(c => c.Status == status).Select(c => c.Name + ": " + c.Message);
        return status + " — " + string.Join(" ", worst) + (recommendation is null ? string.Empty : " " + recommendation);
    }

    /// <summary>FR-OPS-05: posts once when the overall status changes; the previous status is kept in AppSetting.</summary>
    private async Task NotifyOnChangeAsync(HealthStatus status, List<HealthCheck> checks, SiteSettings site, CancellationToken ct)
    {
        var previous = await settings.GetAsync(SettingKeys.LastStatus, ct).ConfigureAwait(false);
        if (previous == status.ToString())
        {
            return;
        }

        await settings.SetAsync(SettingKeys.LastStatus, status.ToString(), ct).ConfigureAwait(false);
        if (previous is null || string.IsNullOrWhiteSpace(site.WebhookUrl))
        {
            return; // no notification for the very first evaluation
        }

        var result = await SendWebhookAsync(site, previous, status, checks, ct).ConfigureAwait(false);
        if (result is not null)
        {
            LogWebhookFailed(logger, result);
        }
    }

    /// <summary>Sends the webhook (also used by the settings page's Test). Returns null on success, otherwise the error.</summary>
    public async Task<string?> SendWebhookAsync(SiteSettings site, string? previous, HealthStatus status, IReadOnlyList<HealthCheck> checks, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(site);
        var (uri, permit, error) = OutboundEndpointRules.Parse(site.WebhookUrl, site.WebhookAllowLan);
        if (uri is null)
        {
            return error;
        }

        var message = $"{site.SiteName}: health is {status}" + (previous is null ? string.Empty : $" (was {previous})") + ". "
            + string.Join(" ", checks.Where(c => c.Status != HealthStatus.OK).Select(c => c.Name + ": " + c.Message));
        var (body, type) = site.WebhookFormat switch
        {
            WebhookFormat.Discord => (JsonSerializer.SerializeToUtf8Bytes(new { content = Core.Audit.AuditRules.Truncate(message, 1900) }), "application/json"),
            WebhookFormat.Json => (JsonSerializer.SerializeToUtf8Bytes(new
            {
                status = status.ToString(),
                previous,
                message,
                checks = checks.Select(c => new { name = c.Name, status = c.Status.ToString(), message = c.Message }),
            }), "application/json"),
            _ => (Encoding.UTF8.GetBytes(message), "text/plain; charset=utf-8"),
        };
        var response = await fetcher.SendAsync(new OutboundRequest(uri, HttpMethod.Post, body, type, new Dictionary<string, string>(), permit,
            TimeSpan.FromSeconds(10), 64 * 1024), ct).ConfigureAwait(false);
        return response.Ok ? null : response.Error;
    }

    private static long Long(string? text) => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    internal static long FileSize(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    internal static long DirectorySize(string path)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        try
        {
            return Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Sum(f => new FileInfo(f).Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Health webhook failed: {Error}")]
    private static partial void LogWebhookFailed(ILogger logger, string error);
}
