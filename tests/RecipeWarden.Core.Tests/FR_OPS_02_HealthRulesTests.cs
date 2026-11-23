using RecipeWarden.Core.Health;

namespace RecipeWarden.Core.Tests;

[Trait("Req", "FR-OPS-02")]
[Trait("Req", "FR-OPS-03")]
public sealed class FR_OPS_02_HealthRulesTests
{
    private const long GB = 1024L * 1024 * 1024;
    private const long MB = 1024L * 1024;
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(25, HealthStatus.OK)]
    [InlineData(15, HealthStatus.Warning)]
    [InlineData(8, HealthStatus.Critical)]
    [Trait("Req", "AC-99")]
    [Trait("Req", "BR-81")]
    public void Free_space_percent_sets_the_status(int freePercent, HealthStatus expected)
    {
        var volume = new VolumeUsage("data", "/var/lib/recipewarden", 100 * GB, freePercent * GB);
        HealthRules.Volume(volume, DiskThresholds.Default).Status.ShouldBe(expected);
    }

    [Theory]
    [InlineData(1500, HealthStatus.Warning)]
    [InlineData(400, HealthStatus.Critical)]
    [Trait("Req", "BR-81")]
    public void Small_volumes_also_use_the_byte_thresholds(long freeMb, HealthStatus expected)
    {
        var volume = new VolumeUsage("data", "/x", 4 * GB, freeMb * MB); // well above 20% in both cases
        HealthRules.Volume(volume, DiskThresholds.Default).Status.ShouldBe(expected);
    }

    [Fact]
    public void An_unmeasurable_volume_is_critical() =>
        HealthRules.Volume(new VolumeUsage("backups", "/mnt/nas", 0, 0), DiskThresholds.Default).Status.ShouldBe(HealthStatus.Critical);

    [Fact]
    [Trait("Req", "AC-99")]
    [Trait("Req", "BR-82")]
    public void Thirty_days_growing_100_MB_a_day_with_3_GB_free_projects_about_30_days()
    {
        var points = Enumerable.Range(0, 30).Select(d => new UsagePoint(Now.AddDays(d - 29), 10 * GB + d * 100 * MB));

        var days = HealthRules.DaysUntilFull(points, 3 * 1000 * MB).ShouldNotBeNull();

        days.ShouldBe(30m, 0.5m);
        HealthRules.Projection("data", days).Status.ShouldBe(HealthStatus.Warning);
        HealthRules.Projection("data", days).Message.ShouldContain("30 days");
    }

    [Fact]
    [Trait("Req", "BR-82")]
    public void Projection_needs_7_snapshots_and_growth()
    {
        var six = Enumerable.Range(0, 6).Select(d => new UsagePoint(Now.AddDays(d), d * GB));
        HealthRules.DaysUntilFull(six, GB).ShouldBeNull();

        var flat = Enumerable.Range(0, 10).Select(d => new UsagePoint(Now.AddDays(d), GB));
        HealthRules.DaysUntilFull(flat, GB).ShouldBeNull();

        var shrinking = Enumerable.Range(0, 10).Select(d => new UsagePoint(Now.AddDays(d), (20 - d) * GB));
        HealthRules.DaysUntilFull(shrinking, GB).ShouldBeNull();

        var sameDay = Enumerable.Range(0, 10).Select(d => new UsagePoint(Now, d * GB));
        HealthRules.DaysUntilFull(sameDay, GB).ShouldBeNull();

        HealthRules.Projection("data", null).Status.ShouldBe(HealthStatus.OK);
    }

    [Fact]
    [Trait("Req", "BR-82")]
    public void Projection_uses_only_the_last_30_snapshots_and_thresholds_are_60_and_14_days()
    {
        // 30 old days of fast growth, then 30 days of 1 GB a day: only the recent trend counts.
        var points = Enumerable.Range(0, 60).Select(d => new UsagePoint(Now.AddDays(d - 59), d < 30 ? d * 10 * GB : 300 * GB + (d - 30) * GB));
        HealthRules.DaysUntilFull(points, 10 * GB).ShouldNotBeNull().ShouldBe(10m, 0.5m);

        HealthRules.Projection("x", 10m).Status.ShouldBe(HealthStatus.Critical);
        HealthRules.Projection("x", 59m).Status.ShouldBe(HealthStatus.Warning);
        HealthRules.Projection("x", 61m).Status.ShouldBe(HealthStatus.OK);
    }

    [Fact]
    [Trait("Req", "BR-83")]
    public void Database_checks()
    {
        HealthRules.Database(false, 100, 0, 0).Status.ShouldBe(HealthStatus.Critical);
        HealthRules.Database(true, 100, 26, 0).Status.ShouldBe(HealthStatus.Warning);
        HealthRules.Database(true, 100, 26, 0).Message.ShouldContain("VACUUM");
        HealthRules.Database(true, 100, 0, 65 * MB).Message.ShouldContain("WAL");
        HealthRules.Database(true, 100, 30, 65 * MB).Message.ShouldContain("; the WAL");
        HealthRules.Database(true, 100, 25, 64 * MB).Status.ShouldBe(HealthStatus.OK);
        HealthRules.Database(null, 0, 0, 0).Message.ShouldContain("No integrity check");
    }

    [Fact]
    [Trait("Req", "BR-84")]
    public void Backup_age_checks()
    {
        HealthRules.Backups(null, null, false, Now).Status.ShouldBe(HealthStatus.Warning);
        HealthRules.Backups(Now.AddHours(-47), null, false, Now).Status.ShouldBe(HealthStatus.OK);
        HealthRules.Backups(Now.AddHours(-49), null, false, Now).Status.ShouldBe(HealthStatus.Warning);
        HealthRules.Backups(Now.AddHours(-25), TimeSpan.FromHours(12), false, Now).Status.ShouldBe(HealthStatus.Warning);
        HealthRules.Backups(Now.AddDays(-6), TimeSpan.FromDays(7), false, Now).Status.ShouldBe(HealthStatus.OK);
        HealthRules.Backups(Now.AddDays(-8), TimeSpan.FromDays(7), false, Now).Status.ShouldBe(HealthStatus.Critical);
        HealthRules.Backups(Now, null, true, Now).Status.ShouldBe(HealthStatus.Critical);
    }

    [Fact]
    [Trait("Req", "BR-84")]
    public void Without_any_backup_the_age_counts_from_when_tracking_started()
    {
        HealthRules.Backups(null, null, false, Now, trackingSince: Now.AddHours(-1)).Status.ShouldBe(HealthStatus.OK);
        HealthRules.Backups(null, null, false, Now, trackingSince: Now.AddHours(-49)).Status.ShouldBe(HealthStatus.Warning);
        HealthRules.Backups(null, TimeSpan.FromDays(7), false, Now, trackingSince: Now.AddDays(-8)).Status.ShouldBe(HealthStatus.Critical);
        HealthRules.Backups(null, null, false, Now, trackingSince: Now.AddHours(-1)).Message.ShouldContain("No backup");
    }

    [Fact]
    [Trait("Req", "BR-80")]
    public void Overall_is_the_worst_check()
    {
        HealthRules.Overall([]).ShouldBe(HealthStatus.OK);
        HealthRules.Overall([new("a", HealthStatus.OK, ""), new("b", HealthStatus.Warning, "")]).ShouldBe(HealthStatus.Warning);
        HealthRules.Overall([new("a", HealthStatus.Critical, ""), new("b", HealthStatus.Warning, "")]).ShouldBe(HealthStatus.Critical);
    }

    [Fact]
    [Trait("Req", "BR-85")]
    [Trait("Req", "AC-99")]
    public void Recommendations_follow_what_takes_the_space()
    {
        HealthRules.Recommendation(HealthStatus.OK, new SpaceByCategory(1, 1, 1, 1, 1)).ShouldBeNull();
        HealthRules.Recommendation(HealthStatus.Warning, new SpaceByCategory(1, 1, 0, 10, 0)).ShouldNotBeNull().ShouldContain("retention");
        HealthRules.Recommendation(HealthStatus.Warning, new SpaceByCategory(5, 5, 0, 1, 0)).ShouldNotBeNull().ShouldContain("larger host");
        HealthRules.Recommendation(HealthStatus.Critical, new SpaceByCategory(1, 1, 5, 1, 5)).ShouldNotBeNull().ShouldContain("Purge");
    }

    [Fact]
    public void Thresholds_are_validated()
    {
        DiskThresholds.Default.Validate().ShouldBeNull();
        (DiskThresholds.Default with { CriticalPercent = 30 }).Validate().ShouldNotBeNull();
        (DiskThresholds.Default with { WarningPercent = 120 }).Validate().ShouldNotBeNull();
    }

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3 GB")]
    public void Bytes_are_formatted(long bytes, string text) => HealthRules.FormatBytes(bytes).ShouldBe(text);

    [Fact]
    public void Ages_are_formatted()
    {
        HealthRules.FormatAge(TimeSpan.FromDays(3)).ShouldBe("3 days");
        HealthRules.FormatAge(TimeSpan.FromHours(5)).ShouldBe("5 hours");
        HealthRules.FormatAge(TimeSpan.FromMinutes(7)).ShouldBe("7 minutes");
    }
}
