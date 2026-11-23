using System.Globalization;

namespace RecipeWarden.Core.Health;

/// <summary>BR-80: OK &lt; Warning &lt; Critical.</summary>
public enum HealthStatus
{
    OK,
    Warning,
    Critical,
}

/// <summary>One evaluated check, shown on /admin/health and stored as a HealthCheckResult (8.5).</summary>
public sealed record HealthCheck(string Name, HealthStatus Status, string Message);

/// <summary>BR-81: disk thresholds (configurable on /admin/settings).</summary>
public sealed record DiskThresholds(decimal WarningPercent, long WarningBytes, decimal CriticalPercent, long CriticalBytes)
{
    public static DiskThresholds Default { get; } = new(20m, 2L * 1024 * 1024 * 1024, 10m, 500L * 1024 * 1024);

    public string? Validate()
    {
        if (WarningPercent is < 0 or > 99 || CriticalPercent is < 0 or > 99 || WarningBytes < 0 || CriticalBytes < 0)
        {
            return "Percentages must be 0–99 and sizes 0 or more.";
        }

        return CriticalPercent > WarningPercent || CriticalBytes > WarningBytes
            ? "Critical thresholds must not be above the Warning thresholds."
            : null;
    }
}

/// <summary>A volume as measured with statvfs (DriveInfo) on the real mount point.</summary>
public sealed record VolumeUsage(string Label, string Path, long TotalBytes, long FreeBytes)
{
    public long UsedBytes => TotalBytes - FreeBytes;

    public decimal FreePercent => TotalBytes <= 0 ? 0m : Math.Round(FreeBytes * 100m / TotalBytes, 1);
}

/// <summary>A daily storage snapshot as the projection sees it.</summary>
public sealed record UsagePoint(DateTimeOffset TakenAt, long UsedBytes);

/// <summary>Space taken by each part of the data directory (FR-OPS-02).</summary>
public sealed record SpaceByCategory(long Database, long Media, long Imports, long Backups, long Logs)
{
    public long Total => Database + Media + Imports + Backups + Logs;
}

/// <summary>
/// BR-80 to BR-85, FR-OPS-02/03, AC-98, AC-99: health status, thresholds, capacity projection and recommendations.
/// Pure: the monitor measures, these rules judge.
/// </summary>
public static class HealthRules
{
    public const int ProjectionWindow = 30;
    public const int MinSnapshotsForProjection = 7;
    public const int ProjectionWarningDays = 60;
    public const int ProjectionCriticalDays = 14;
    public const long WalWarningBytes = 64L * 1024 * 1024;
    public const decimal FreePagesWarningShare = 0.25m;
    public static readonly TimeSpan NoScheduleBackupAge = TimeSpan.FromHours(48);
    public static readonly TimeSpan CriticalBackupAge = TimeSpan.FromDays(7);

    /// <summary>BR-80: the worst status of all checks (OK when there are none).</summary>
    public static HealthStatus Overall(IEnumerable<HealthCheck> checks) =>
        checks.Select(c => c.Status).DefaultIfEmpty(HealthStatus.OK).Max();

    /// <summary>BR-81: Warning below 20% or 2 GB free; Critical below 10% or 500 MB free.</summary>
    public static HealthCheck Volume(VolumeUsage volume, DiskThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentNullException.ThrowIfNull(thresholds);
        var name = "Disk: " + volume.Label;
        if (volume.TotalBytes <= 0)
        {
            return new HealthCheck(name, HealthStatus.Critical, $"{volume.Path} could not be measured (not mounted?).");
        }

        var pct = volume.FreeBytes * 100m / volume.TotalBytes;
        var text = string.Create(CultureInfo.InvariantCulture, $"{FormatBytes(volume.FreeBytes)} free of {FormatBytes(volume.TotalBytes)} ({volume.FreePercent:0.#}% free)");
        if (pct < thresholds.CriticalPercent || volume.FreeBytes < thresholds.CriticalBytes)
        {
            return new HealthCheck(name, HealthStatus.Critical, text);
        }

        return pct < thresholds.WarningPercent || volume.FreeBytes < thresholds.WarningBytes
            ? new HealthCheck(name, HealthStatus.Warning, text)
            : new HealthCheck(name, HealthStatus.OK, text);
    }

    /// <summary>
    /// BR-82: linear trend of used bytes over the last 30 daily snapshots. Returns null unless there are at least 7
    /// snapshots and growth is positive.
    /// </summary>
    public static decimal? DaysUntilFull(IEnumerable<UsagePoint> snapshots, long freeBytes)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        var points = snapshots.OrderBy(s => s.TakenAt).TakeLast(ProjectionWindow).ToList();
        if (points.Count < MinSnapshotsForProjection)
        {
            return null;
        }

        var origin = points[0].TakenAt;
        var xs = points.Select(p => (double)(p.TakenAt - origin).TotalDays).ToArray();
        var ys = points.Select(p => (double)p.UsedBytes).ToArray();
        var mx = xs.Average();
        var my = ys.Average();
        var sxx = xs.Sum(x => (x - mx) * (x - mx));
        if (sxx <= 0)
        {
            return null;
        }

        var slope = xs.Zip(ys).Sum(p => (p.First - mx) * (p.Second - my)) / sxx; // bytes per day
        if (slope <= 0)
        {
            return null;
        }

        return Math.Round((decimal)(Math.Max(0, freeBytes) / slope), 1);
    }

    /// <summary>BR-82: Warning under 60 days, Critical under 14.</summary>
    public static HealthCheck Projection(string label, decimal? daysUntilFull)
    {
        var name = "Capacity: " + label;
        if (daysUntilFull is not { } days)
        {
            return new HealthCheck(name, HealthStatus.OK, "Not enough growth history for a projection (needs 7 daily snapshots and growth).");
        }

        var text = string.Create(CultureInfo.InvariantCulture, $"About {Math.Floor(days):0} days until full at the current growth.");
        return days < ProjectionCriticalDays ? new HealthCheck(name, HealthStatus.Critical, text)
            : days < ProjectionWarningDays ? new HealthCheck(name, HealthStatus.Warning, text)
            : new HealthCheck(name, HealthStatus.OK, text);
    }

    /// <summary>
    /// BR-83: Critical if the last integrity check failed; Warning if reclaimable free pages exceed 25% of the file
    /// or the WAL file exceeds 64 MB.
    /// </summary>
    public static HealthCheck Database(bool? lastIntegrityOk, long pageCount, long freePages, long walBytes)
    {
        const string name = "Database";
        if (lastIntegrityOk == false)
        {
            return new HealthCheck(name, HealthStatus.Critical, "The last integrity check failed. Restore from a recent backup.");
        }

        var problems = new List<string>();
        if (pageCount > 0 && freePages > pageCount * FreePagesWarningShare)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"{freePages * 100 / pageCount}% of the file is free pages — run Compact (VACUUM)"));
        }

        if (walBytes > WalWarningBytes)
        {
            problems.Add($"the WAL file is {FormatBytes(walBytes)} (over 64 MB)");
        }

        if (problems.Count > 0)
        {
            return new HealthCheck(name, HealthStatus.Warning, char.ToUpperInvariant(problems[0][0]) + string.Join("; ", problems)[1..] + ".");
        }

        return new HealthCheck(name, HealthStatus.OK, lastIntegrityOk is null ? "No integrity check has run yet." : "Integrity check passed.");
    }

    /// <summary>
    /// BR-84: Warning when the last successful backup is older than 2x the schedule interval (48 h without a
    /// schedule); Critical after 7 days or when a backup destination is unreachable.
    /// </summary>
    /// <param name="trackingSince">
    /// ADR 0037: when the app first measured health. With no backup yet, the age counts from then, so a fresh install is
    /// not flagged until the same 48 hours (or 2x the schedule) have passed.
    /// </param>
    public static HealthCheck Backups(DateTimeOffset? lastSuccess, TimeSpan? shortestInterval, bool anyDestinationUnreachable, DateTimeOffset now,
        DateTimeOffset? trackingSince = null)
    {
        const string name = "Backups";
        if (anyDestinationUnreachable)
        {
            return new HealthCheck(name, HealthStatus.Critical, "A backup folder is unreachable: " + BackupFolderHint);
        }

        var limit = shortestInterval is { } i ? i * 2 : NoScheduleBackupAge;
        if (lastSuccess is not { } last)
        {
            var waited = now - (trackingSince ?? now - limit - TimeSpan.FromSeconds(1));
            var none = "No backup has been made yet. Use Back up now or add a schedule.";
            return waited > CriticalBackupAge ? new HealthCheck(name, HealthStatus.Critical, none)
                : waited > limit ? new HealthCheck(name, HealthStatus.Warning, none)
                : new HealthCheck(name, HealthStatus.OK, none);
        }

        var age = now - last;
        var text = "Last successful backup " + FormatAge(age) + " ago.";
        if (age > CriticalBackupAge)
        {
            return new HealthCheck(name, HealthStatus.Critical, text);
        }

        return age > limit ? new HealthCheck(name, HealthStatus.Warning, text) : new HealthCheck(name, HealthStatus.OK, text);
    }

    public const string BackupFolderHint = "check that the share is mounted and has the marker file, then press Test on /admin/backups.";

    /// <summary>
    /// BR-85: an explicit recommendation when a volume is below a threshold: mostly backups → tighten retention or
    /// move backups off the volume; database or images dominant → add disk or move to a larger host.
    /// </summary>
    public static string? Recommendation(HealthStatus diskStatus, SpaceByCategory space)
    {
        ArgumentNullException.ThrowIfNull(space);
        if (diskStatus == HealthStatus.OK)
        {
            return null;
        }

        if (space.Total > 0 && space.Backups * 2 >= space.Total)
        {
            return "Backups take most of the space: tighten retention on /admin/backups, or send backups to a folder on another volume (e.g. the NAS).";
        }

        if (space.Imports + space.Logs > space.Database + space.Media)
        {
            return "Import files and logs take most of the space: run \"Purge expired imports and trash\" and check the log retention.";
        }

        return "Recipes and images take most of the space: add disk to this host, or back up and restore to a larger host (see the move guide).";
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var i = 0;
        while (Math.Abs(value) >= 1024 && i < units.Length - 1)
        {
            value /= 1024;
            i++;
        }

        return i == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{value:0} {units[i]}")
            : string.Create(CultureInfo.InvariantCulture, $"{value:0.#} {units[i]}");
    }

    public static string FormatAge(TimeSpan age) =>
        age.TotalDays >= 2 ? string.Create(CultureInfo.InvariantCulture, $"{Math.Floor(age.TotalDays):0} days")
        : age.TotalHours >= 2 ? string.Create(CultureInfo.InvariantCulture, $"{Math.Floor(age.TotalHours):0} hours")
        : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, Math.Floor(age.TotalMinutes)):0} minutes");
}
