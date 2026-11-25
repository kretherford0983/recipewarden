using RecipeWarden.Core.Backup;

namespace RecipeWarden.Core.Tests;

[Trait("Req", "FR-BKP-02")]
[Trait("Req", "FR-BKP-14")]
public sealed class FR_BKP_02_ScheduleRulesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 2, 30, 0, TimeSpan.Zero);

    /// <summary>A daily 02:30 cron, evaluated by hand (Cronos does this in the app).</summary>
    private static DateTimeOffset? NextDaily(DateTimeOffset after)
    {
        var candidate = new DateTimeOffset(after.Year, after.Month, after.Day, 2, 30, 0, TimeSpan.Zero);
        return candidate > after ? candidate : candidate.AddDays(1);
    }

    [Theory]
    [InlineData(ScheduleFrequency.Daily, 2, 30, DayOfWeek.Sunday, "30 2 * * *")]
    [InlineData(ScheduleFrequency.Weekly, 3, 5, DayOfWeek.Saturday, "5 3 * * 6")]
    [InlineData(ScheduleFrequency.Weekly, 0, 0, DayOfWeek.Sunday, "0 0 * * 0")]
    public void Picker_becomes_cron_and_back(ScheduleFrequency frequency, int hour, int minute, DayOfWeek day, string cron)
    {
        BackupScheduleRules.ToCron(frequency, new TimeOnly(hour, minute), day).ShouldBe(cron);
        BackupScheduleRules.FromCron(cron).ShouldBe((frequency, new TimeOnly(hour, minute), frequency == ScheduleFrequency.Daily ? DayOfWeek.Sunday : day));
    }

    [Fact]
    public void Other_cron_expressions_stay_cron()
    {
        BackupScheduleRules.ToCron(ScheduleFrequency.Cron, default, cron: " 0 */6 * * * ").ShouldBe("0 */6 * * *");
        BackupScheduleRules.FromCron("0 */6 * * *").Frequency.ShouldBe(ScheduleFrequency.Cron);
        BackupScheduleRules.Describe("0 */6 * * *").ShouldBe("Cron: 0 */6 * * *");
        BackupScheduleRules.Describe("30 2 * * *").ShouldBe("Daily at 02:30");
        BackupScheduleRules.Describe("0 3 * * 7").ShouldBe("Weekly on Sunday at 03:00");
    }

    [Fact]
    [Trait("Req", "AC-89")]
    public void Failed_run_is_retried_after_1_then_2_then_4_hours_and_the_next_scheduled_run_stays_on_time()
    {
        var timing = new ScheduleTiming(null, T0, 0, null);
        BackupScheduleRules.Due(timing, T0).ShouldBe(ScheduledRunReason.Scheduled);

        timing = BackupScheduleRules.AfterRun(timing, ScheduledRunReason.Scheduled, succeeded: false, T0, NextDaily);
        timing.NextRetryAt.ShouldBe(T0.AddHours(1));
        timing.NextRunAt.ShouldBe(T0.AddDays(1));
        BackupScheduleRules.Due(timing, T0.AddMinutes(59)).ShouldBeNull();

        var now = T0.AddHours(1);
        BackupScheduleRules.Due(timing, now).ShouldBe(ScheduledRunReason.Retry);
        timing = BackupScheduleRules.AfterRun(timing, ScheduledRunReason.Retry, false, now, NextDaily);
        timing.NextRetryAt.ShouldBe(now.AddHours(2));

        now = now.AddHours(2);
        timing = BackupScheduleRules.AfterRun(timing, ScheduledRunReason.Retry, false, now, NextDaily);
        timing.NextRetryAt.ShouldBe(now.AddHours(4));
        timing.RetryAttempt.ShouldBe(3);
        timing.NextRunAt.ShouldBe(T0.AddDays(1), "retries never move the next scheduled run");

        BackupScheduleRules.Due(timing, T0.AddDays(1)).ShouldBe(ScheduledRunReason.Scheduled);
        timing = BackupScheduleRules.AfterRun(timing, ScheduledRunReason.Scheduled, true, T0.AddDays(1), NextDaily);
        timing.RetryAttempt.ShouldBe(0);
        timing.NextRetryAt.ShouldBeNull();
        timing.LastRunAt.ShouldBe(T0.AddDays(1));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(5, 16)]
    [InlineData(6, 24)]
    [InlineData(40, 24)]
    [InlineData(0, 1)]
    public void Backoff_doubles_up_to_24_hours(int attempt, int hours) =>
        BackupScheduleRules.RetryDelay(attempt).ShouldBe(TimeSpan.FromHours(hours));

    [Fact]
    [Trait("Req", "AC-89")]
    public void A_run_missed_while_stopped_runs_once_at_the_next_start()
    {
        var timing = new ScheduleTiming(T0.AddDays(-5), T0.AddDays(-4), 0, null); // stopped for 4 days
        var start = T0;

        BackupScheduleRules.Due(timing, start).ShouldBe(ScheduledRunReason.Scheduled);
        timing = BackupScheduleRules.AfterRun(timing, ScheduledRunReason.Scheduled, true, start, NextDaily);
        timing.NextRunAt.ShouldBe(T0.AddDays(1), "the four missed runs are not repeated");
        BackupScheduleRules.Due(timing, start.AddMinutes(1)).ShouldBeNull();
    }

    [Fact]
    public void Interval_is_the_gap_between_the_next_two_times()
    {
        BackupScheduleRules.Interval(T0, NextDaily).ShouldBe(TimeSpan.FromDays(1));
        BackupScheduleRules.Interval(T0, _ => null).ShouldBeNull();
    }

    [Theory]
    [InlineData(null, "name")]
    [InlineData("  ", "name")]
    public void Name_is_required(string? name, string fragment) =>
        BackupScheduleRules.ValidateName(name).ShouldNotBeNull().ShouldContain(fragment);

    [Fact]
    public void Name_length_is_capped()
    {
        BackupScheduleRules.ValidateName(new string('a', 81)).ShouldNotBeNull();
        BackupScheduleRules.ValidateName("Nightly to the NAS").ShouldBeNull();
    }
}
