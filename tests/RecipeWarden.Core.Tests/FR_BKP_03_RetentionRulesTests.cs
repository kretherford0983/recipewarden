using RecipeWarden.Core.Backup;

namespace RecipeWarden.Core.Tests;

[Trait("Req", "FR-BKP-03")]
[Trait("Req", "BR-61")]
public sealed class FR_BKP_03_RetentionRulesTests
{
    private static readonly TimeSpan Local = TimeSpan.FromHours(-6);

    private static DateTimeOffset Day(int month, int day, int hour = 2, int minute = 30) => new(2026, month, day, hour, minute, 0, Local);

    [Fact]
    [Trait("Req", "AC-81")]
    public void Daily_schedule_keeps_exactly_7_daily_4_weekly_6_monthly_after_60_days_and_never_the_newest()
    {
        var policy = new RetentionPolicy(KeepLast: 0, Daily: 7, Weekly: 4, Monthly: 6);
        var kept = new List<RetainedArchive>();
        var start = Day(1, 1);
        for (var i = 0; i < 60; i++)
        {
            var at = start.AddDays(i);
            kept.Add(new RetainedArchive(i + 1, at, BackupKind.Scheduled));
            var delete = RetentionRules.SelectForDeletion(kept, policy, at);
            delete.ShouldNotContain(i + 1, "the newest archive is never deleted");
            kept.RemoveAll(a => delete.Contains(a.Id));
        }

        // Day 60 is Sunday 1 March 2026. Worked by hand:
        //  daily:   23 Feb .. 1 Mar (7)
        //  weekly:  newest of the ISO weeks ending 1 Mar, 22 Feb, 15 Feb, 8 Feb
        //  monthly: newest of March (1 Mar), February (28 Feb), January (31 Jan); only 3 months exist
        DateTimeOffset[] expected =
        [
            Day(1, 31), Day(2, 8), Day(2, 15), Day(2, 22),
            Day(2, 23), Day(2, 24), Day(2, 25), Day(2, 26), Day(2, 27), Day(2, 28), Day(3, 1),
        ];
        kept.Select(a => a.CompletedAt).Order().ShouldBe(expected);
    }

    [Fact]
    public void Keep_last_keeps_the_newest_n_whatever_their_dates()
    {
        var archives = Enumerable.Range(1, 10).Select(i => new RetainedArchive(i, Day(1, 1, 0, i), BackupKind.Scheduled)).ToList();

        var delete = RetentionRules.SelectForDeletion(archives, new RetentionPolicy(3, 0, 0, 0), Day(1, 2));

        delete.Order().ShouldBe([1, 2, 3, 4, 5, 6, 7]);
    }

    [Fact]
    public void The_newest_archive_survives_even_a_policy_that_keeps_nothing_else()
    {
        var archives = new[] { new RetainedArchive(1, Day(1, 1), BackupKind.Manual), new RetainedArchive(2, Day(1, 2), BackupKind.Manual) };

        RetentionRules.SelectForDeletion(archives, new RetentionPolicy(0, 0, 0, 0), Day(1, 3)).ShouldBe([1]);
    }

    [Fact]
    public void Pre_restore_and_pre_upgrade_archives_younger_than_30_days_are_never_deleted()
    {
        var now = Day(3, 1);
        var archives = new[]
        {
            new RetainedArchive(1, now.AddDays(-29), BackupKind.PreRestore),
            new RetainedArchive(2, now.AddDays(-29), BackupKind.PreUpgrade),
            new RetainedArchive(3, now.AddDays(-31), BackupKind.PreUpgrade),
            new RetainedArchive(4, now.AddDays(-1), BackupKind.Scheduled),
        };

        RetentionRules.SelectForDeletion(archives, new RetentionPolicy(1, 0, 0, 0), now).ShouldBe([3]);
        RetentionRules.IsProtected(BackupKind.PreRestore, now.AddDays(-29), now).ShouldBeTrue();
        RetentionRules.IsProtected(BackupKind.PreRestore, now.AddDays(-31), now).ShouldBeFalse();
        RetentionRules.IsProtected(BackupKind.Manual, now, now).ShouldBeFalse();
    }

    [Fact]
    public void Nothing_to_retain_deletes_nothing() =>
        RetentionRules.SelectForDeletion([], RetentionPolicy.Default, Day(1, 1)).ShouldBeEmpty();

    [Theory]
    [InlineData(0, 0, 0, 0, "at least one")]
    [InlineData(-1, 7, 0, 0, "between 0")]
    [InlineData(1001, 0, 0, 0, "between 0")]
    public void Invalid_policies_are_explained(int last, int daily, int weekly, int monthly, string fragment) =>
        new RetentionPolicy(last, daily, weekly, monthly).Validate().ShouldNotBeNull().ShouldContain(fragment);

    [Fact]
    public void Default_policy_is_valid_and_describable()
    {
        RetentionPolicy.Default.Validate().ShouldBeNull();
        RetentionPolicy.Default.ToString().ShouldBe("last 3, 7 daily, 4 weekly, 6 monthly");
    }
}
