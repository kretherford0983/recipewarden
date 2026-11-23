using System.Globalization;
using System.Text.RegularExpressions;

namespace RecipeWarden.Core.Backup;

/// <summary>FR-BKP-02: the simple picker, or a cron expression.</summary>
public enum ScheduleFrequency
{
    Daily,
    Weekly,
    Cron,
}

/// <summary>Why a schedule runs now.</summary>
public enum ScheduledRunReason
{
    /// <summary>The cron time arrived (or was missed while the app was stopped, FR-BKP-14).</summary>
    Scheduled,

    /// <summary>A failed run is retried with backoff (FR-BKP-14).</summary>
    Retry,
}

/// <summary>The timing part of a BackupSchedule row (8.5).</summary>
public sealed record ScheduleTiming(DateTimeOffset? LastRunAt, DateTimeOffset? NextRunAt, int RetryAttempt, DateTimeOffset? NextRetryAt);

/// <summary>
/// FR-BKP-02, FR-BKP-14, AC-89: when a schedule runs, retry backoff, and missed runs. Cron evaluation itself is done
/// by the caller (Cronos in Infrastructure) and passed in as <c>next</c>, so these rules stay free of packages.
/// </summary>
public static partial class BackupScheduleRules
{
    public const int MaxNameLength = 80;
    public const int MaxCronLength = 120;
    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromHours(24);

    /// <summary>The cron expression for the simple picker: "30 2 * * *" daily, "30 2 * * 0" weekly on Sunday.</summary>
    public static string ToCron(ScheduleFrequency frequency, TimeOnly at, DayOfWeek day = DayOfWeek.Sunday, string? cron = null) => frequency switch
    {
        ScheduleFrequency.Daily => string.Create(CultureInfo.InvariantCulture, $"{at.Minute} {at.Hour} * * *"),
        ScheduleFrequency.Weekly => string.Create(CultureInfo.InvariantCulture, $"{at.Minute} {at.Hour} * * {(int)day}"),
        _ => (cron ?? string.Empty).Trim(),
    };

    /// <summary>Reads a stored cron back into the picker when it is a plain daily or weekly time.</summary>
    public static (ScheduleFrequency Frequency, TimeOnly At, DayOfWeek Day) FromCron(string cron)
    {
        var m = SimpleCron().Match(cron ?? string.Empty);
        if (m.Success)
        {
            var at = new TimeOnly(int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture));
            if (m.Groups["d"].Value == "*")
            {
                return (ScheduleFrequency.Daily, at, DayOfWeek.Sunday);
            }

            var day = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture) % 7;
            return (ScheduleFrequency.Weekly, at, (DayOfWeek)day);
        }

        return (ScheduleFrequency.Cron, new TimeOnly(2, 30), DayOfWeek.Sunday);
    }

    /// <summary>A human description: "Daily at 02:30", "Weekly on Sunday at 03:00", or the cron text.</summary>
    public static string Describe(string cron)
    {
        var (frequency, at, day) = FromCron(cron);
        return frequency switch
        {
            ScheduleFrequency.Daily => string.Create(CultureInfo.InvariantCulture, $"Daily at {at:HH\\:mm}"),
            ScheduleFrequency.Weekly => string.Create(CultureInfo.InvariantCulture, $"Weekly on {day} at {at:HH\\:mm}"),
            _ => "Cron: " + cron,
        };
    }

    public static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Give the schedule a name, e.g. \"Nightly to the NAS\".";
        }

        return name.Trim().Length > MaxNameLength ? $"The name can be at most {MaxNameLength} characters." : null;
    }

    /// <summary>Which run, if any, is due now. A run that was due while the app was stopped is due at the next start.</summary>
    public static ScheduledRunReason? Due(ScheduleTiming timing, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(timing);
        if (timing.NextRunAt is { } next && next <= now)
        {
            return ScheduledRunReason.Scheduled;
        }

        return timing.NextRetryAt is { } retry && retry <= now ? ScheduledRunReason.Retry : null;
    }

    /// <summary>
    /// The timing after a run. A scheduled run moves <see cref="ScheduleTiming.NextRunAt"/> to the next cron time
    /// after now, so missed runs are not repeated; a retry leaves it alone, so the next scheduled run is still on time.
    /// A failure schedules a retry after 1, 2, 4 … hours (at most 24); a success clears the retry.
    /// </summary>
    public static ScheduleTiming AfterRun(ScheduleTiming timing, ScheduledRunReason reason, bool succeeded, DateTimeOffset now,
        Func<DateTimeOffset, DateTimeOffset?> next)
    {
        ArgumentNullException.ThrowIfNull(timing);
        ArgumentNullException.ThrowIfNull(next);

        var nextRun = reason == ScheduledRunReason.Scheduled ? next(now) : timing.NextRunAt;
        if (succeeded)
        {
            return new ScheduleTiming(now, nextRun, 0, null);
        }

        var attempt = timing.RetryAttempt + 1;
        return new ScheduleTiming(now, nextRun, attempt, now + RetryDelay(attempt));
    }

    /// <summary>1 h after the first failure, then 2, 4, 8, 16, then 24 h.</summary>
    public static TimeSpan RetryDelay(int attempt)
    {
        if (attempt < 1)
        {
            return TimeSpan.FromHours(1);
        }

        var hours = attempt >= 6 ? MaxRetryDelay.TotalHours : Math.Pow(2, attempt - 1);
        return TimeSpan.FromHours(Math.Min(hours, MaxRetryDelay.TotalHours));
    }

    /// <summary>BR-84: the schedule's interval, measured between its next two cron times.</summary>
    public static TimeSpan? Interval(DateTimeOffset now, Func<DateTimeOffset, DateTimeOffset?> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        var a = next(now);
        var b = a is null ? null : next(a.Value);
        return a is not null && b is not null ? b.Value - a.Value : null;
    }

    [GeneratedRegex(@"^(?<m>[0-5]?\d) (?<h>[01]?\d|2[0-3]) \* \* (?<d>\*|[0-7])$")]
    private static partial Regex SimpleCron();
}
