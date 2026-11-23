using Cronos;
using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Backup;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Settings;

namespace RecipeWarden.Infrastructure.Backup;

/// <summary>A schedule as edited on /admin/backups (input DTO, SEC-13).</summary>
public sealed record ScheduleEdit(
    string? Name,
    ScheduleFrequency Frequency,
    TimeOnly At,
    DayOfWeek Day,
    string? Cron,
    string? Folder,
    RetentionPolicy Retention,
    bool IsEnabled);

public sealed record ScheduleSave(BackupSchedule? Schedule, string? Error);

/// <summary>
/// FR-BKP-02, 10, 14, SEC-74: schedule CRUD and the scheduler's "run what is due". Cron times are the server's local
/// time zone (Cronos). The destination must match an allowed folder when saved and again when run.
/// </summary>
public sealed class BackupScheduleService(AppDbContext db, BackupFolders folders, BackupService backups, SettingsStore settings, TimeProvider clock)
{
    public async Task<IReadOnlyList<BackupSchedule>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.BackupSchedules.AsNoTracking().OrderBy(s => s.Name).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<ScheduleSave> SaveAsync(int? id, ScheduleEdit edit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var error = BackupScheduleRules.ValidateName(edit.Name) ?? edit.Retention.Validate();
        var cron = BackupScheduleRules.ToCron(edit.Frequency, edit.At, edit.Day, edit.Cron);
        if (error is null && ParseCron(cron) is null)
        {
            error = "That cron expression is not valid. Use five fields, e.g. \"30 2 * * *\" for 02:30 every day.";
        }

        // SEC-74: the saved value is the allowed entry, never the request text.
        var folder = folders.Resolve(edit.Folder);
        if (error is null && folder is null)
        {
            error = "Choose one of the allowed backup folders.";
        }

        if (error is null && folder != folders.Local && !(await settings.GetAsync(SettingKeys.BackupPublicKey, cancellationToken).ConfigureAwait(false) is not null))
        {
            error = "Backups leaving this server must be encrypted (FR-BKP-15). Create the encryption key first.";
        }

        if (error is not null)
        {
            return new ScheduleSave(null, error);
        }

        var now = clock.GetUtcNow();
        BackupSchedule? schedule;
        if (id is { } existing)
        {
            schedule = await db.BackupSchedules.FindAsync([existing], cancellationToken).ConfigureAwait(false);
            if (schedule is null)
            {
                return new ScheduleSave(null, "That schedule no longer exists.");
            }
        }
        else
        {
            schedule = new BackupSchedule { CreatedAt = now };
            db.BackupSchedules.Add(schedule);
        }

        schedule.Name = edit.Name!.Trim();
        schedule.Cron = cron;
        schedule.DestinationFolder = folder!;
        schedule.KeepLast = edit.Retention.KeepLast;
        schedule.KeepDaily = edit.Retention.Daily;
        schedule.KeepWeekly = edit.Retention.Weekly;
        schedule.KeepMonthly = edit.Retention.Monthly;
        schedule.IsEnabled = edit.IsEnabled;
        schedule.UpdatedAt = now;
        schedule.NextRunAt = edit.IsEnabled ? Next(cron, now) : null;
        schedule.RetryAttempt = 0;
        schedule.NextRetryAt = null;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ScheduleSave(schedule, null);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var schedule = await db.BackupSchedules.FindAsync([id], cancellationToken).ConfigureAwait(false);
        if (schedule is null)
        {
            return false;
        }

        db.BackupSchedules.Remove(schedule); // its archives and records stay; retention simply stops
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>FR-BKP-05 "Run now": a run for this schedule, with its retention, outside its timing.</summary>
    public async Task<BackupOutcome?> RunNowAsync(int id, CancellationToken cancellationToken = default)
    {
        var schedule = await db.BackupSchedules.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false);
        return schedule is null
            ? null
            : await backups.RunAsync(new BackupRequest(BackupKind.Scheduled, schedule.DestinationFolder, schedule.Id), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>FR-BKP-14, AC-89: runs every enabled schedule that is due (scheduled, missed, or a retry). Returns the runs made.</summary>
    public async Task<int> RunDueAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var due = (await db.BackupSchedules.Where(s => s.IsEnabled).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(s => (Schedule: s, Reason: BackupScheduleRules.Due(s.Timing, now)))
            .Where(d => d.Reason is not null)
            .ToList();

        foreach (var (schedule, reason) in due)
        {
            var outcome = await backups.RunAsync(new BackupRequest(BackupKind.Scheduled, schedule.DestinationFolder, schedule.Id), cancellationToken)
                .ConfigureAwait(false);
            var cron = schedule.Cron;
            schedule.Apply(BackupScheduleRules.AfterRun(schedule.Timing, reason!.Value, outcome.Ok, clock.GetUtcNow(), t => Next(cron, t)));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return due.Count;
    }

    /// <summary>Runs left as "Running" by a stopped process are marked failed at start.</summary>
    public async Task<int> CloseInterruptedAsync(CancellationToken cancellationToken = default)
    {
        var stale = await db.BackupRecords.Where(r => r.Status == BackupStatus.Running).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var r in stale)
        {
            r.Status = BackupStatus.Failed;
            r.CompletedAt = clock.GetUtcNow();
            r.Error = "The service stopped while this backup was running.";
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return stale.Count;
    }

    /// <summary>BR-84: the shortest interval among enabled schedules, for the backup-age check.</summary>
    public static TimeSpan? ShortestInterval(IEnumerable<BackupSchedule> schedules, DateTimeOffset now) =>
        schedules.Where(s => s.IsEnabled)
            .Select(s => BackupScheduleRules.Interval(now, t => Next(s.Cron, t)))
            .Where(i => i is not null)
            .Min();

    public static DateTimeOffset? Next(string cron, DateTimeOffset after) =>
        ParseCron(cron)?.GetNextOccurrence(after, TimeZoneInfo.Local);

    public static CronExpression? ParseCron(string? cron)
    {
        if (string.IsNullOrWhiteSpace(cron) || cron.Length > BackupScheduleRules.MaxCronLength)
        {
            return null;
        }

        try
        {
            return CronExpression.Parse(cron, CronFormat.Standard);
        }
        catch (CronFormatException)
        {
            return null;
        }
    }
}
