using System.Globalization;

namespace RecipeWarden.Core.Backup;

/// <summary>Section 8.5 BackupRecord.Kind.</summary>
public enum BackupKind
{
    Manual,
    Scheduled,
    PreRestore,
    PreUpgrade,
}

/// <summary>
/// FR-BKP-03: how many archives a schedule keeps. Zero turns a tier off. <see cref="KeepLast"/> keeps the newest N
/// whatever their dates; the tiers keep the newest archive of each of the last N days, ISO weeks and months that
/// have an archive (grandfather-father-son).
/// </summary>
public sealed record RetentionPolicy(int KeepLast, int Daily, int Weekly, int Monthly)
{
    public const int MaxPerTier = 1000;

    /// <summary>ADR 0037: a new schedule keeps the last 3 plus 7 daily, 4 weekly and 6 monthly archives.</summary>
    public static RetentionPolicy Default { get; } = new(3, 7, 4, 6);

    public string? Validate()
    {
        int[] all = [KeepLast, Daily, Weekly, Monthly];
        if (all.Any(n => n < 0 || n > MaxPerTier))
        {
            return $"Each retention number must be between 0 and {MaxPerTier}.";
        }

        return all.Sum() == 0 ? "Keep at least one archive (set any retention number above 0)." : null;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"last {KeepLast}, {Daily} daily, {Weekly} weekly, {Monthly} monthly");
}

/// <summary>An archive as retention sees it: one of the schedule's own successful runs (BR-61).</summary>
/// <param name="Id">BackupRecord id.</param>
/// <param name="CompletedAt">When the run finished, in the server's local time (day boundaries follow it).</param>
public sealed record RetainedArchive(int Id, DateTimeOffset CompletedAt, BackupKind Kind);

/// <summary>
/// BR-61, FR-BKP-03, AC-81: which archives retention deletes. The caller passes only archives that its own successful
/// runs recorded in that folder; files are never chosen by name pattern. Never deletes the most recent archive, or a
/// pre-restore or pre-upgrade archive younger than 30 days.
/// </summary>
public static class RetentionRules
{
    public static readonly TimeSpan ProtectedSafetyAge = TimeSpan.FromDays(30);

    public static IReadOnlyList<int> SelectForDeletion(IEnumerable<RetainedArchive> archives, RetentionPolicy policy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(archives);
        ArgumentNullException.ThrowIfNull(policy);

        var newestFirst = archives.OrderByDescending(a => a.CompletedAt).ThenByDescending(a => a.Id).ToList();
        if (newestFirst.Count == 0)
        {
            return [];
        }

        var keep = new HashSet<int> { newestFirst[0].Id }; // never delete the newest
        foreach (var a in newestFirst.Take(policy.KeepLast))
        {
            keep.Add(a.Id);
        }

        KeepNewestPerPeriod(newestFirst, policy.Daily, a => a.CompletedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), keep);
        KeepNewestPerPeriod(newestFirst, policy.Weekly, a => IsoWeek(a.CompletedAt), keep);
        KeepNewestPerPeriod(newestFirst, policy.Monthly, a => a.CompletedAt.ToString("yyyy-MM", CultureInfo.InvariantCulture), keep);

        foreach (var a in newestFirst.Where(IsSafetyKind))
        {
            if (now - a.CompletedAt < ProtectedSafetyAge)
            {
                keep.Add(a.Id);
            }
        }

        return newestFirst.Where(a => !keep.Contains(a.Id)).Select(a => a.Id).ToList();
    }

    /// <summary>BR-61: pre-restore and pre-upgrade archives younger than 30 days are never deleted by anyone.</summary>
    public static bool IsProtected(BackupKind kind, DateTimeOffset completedAt, DateTimeOffset now) =>
        kind is BackupKind.PreRestore or BackupKind.PreUpgrade && now - completedAt < ProtectedSafetyAge;

    private static bool IsSafetyKind(RetainedArchive a) => a.Kind is BackupKind.PreRestore or BackupKind.PreUpgrade;

    private static void KeepNewestPerPeriod(List<RetainedArchive> newestFirst, int periods, Func<RetainedArchive, string> period, HashSet<int> keep)
    {
        if (periods <= 0)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in newestFirst)
        {
            if (seen.Add(period(a)))
            {
                keep.Add(a.Id); // newest archive of a period not seen yet
                if (seen.Count == periods)
                {
                    return;
                }
            }
        }
    }

    private static string IsoWeek(DateTimeOffset at) =>
        string.Create(CultureInfo.InvariantCulture, $"{ISOWeek.GetYear(at.DateTime)}-W{ISOWeek.GetWeekOfYear(at.DateTime):00}");
}
