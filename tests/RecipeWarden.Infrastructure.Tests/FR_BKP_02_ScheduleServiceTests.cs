using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Backup;
using RecipeWarden.Infrastructure.Backup;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Tests.Shared;

namespace RecipeWarden.Infrastructure.Tests;

[Trait("Req", "FR-BKP-02")]
[Trait("Req", "FR-BKP-10")]
public sealed class FR_BKP_02_ScheduleServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ScheduleEdit Edit(string folder, RetentionPolicy? retention = null, string? name = "Nightly") =>
        new(name, ScheduleFrequency.Daily, new TimeOnly(2, 30), DayOfWeek.Sunday, null, folder, retention ?? RetentionPolicy.Default, true);

    [Fact]
    [Trait("Req", "AC-87")]
    [Trait("Req", "SEC-74")]
    public async Task Saving_a_folder_not_in_the_list_is_refused_and_the_saved_value_is_the_config_entry()
    {
        await using var s = await BackupTestScope.CreateAsync();
        await s.CreateKeyAsync();

        var refused = await s.WithAsync(sp => sp.GetRequiredService<BackupScheduleService>().SaveAsync(null, Edit("/etc"), Ct));
        refused.Error.ShouldNotBeNull().ShouldContain("allowed backup folders");

        var saved = await s.WithAsync(sp => sp.GetRequiredService<BackupScheduleService>().SaveAsync(null, Edit("//" + s.Nas.TrimStart('/') + "///"), Ct));
        saved.Error.ShouldBeNull();
        saved.Schedule!.DestinationFolder.ShouldBe(s.Nas);
        saved.Schedule.Cron.ShouldBe("30 2 * * *");
        saved.Schedule.NextRunAt.ShouldNotBeNull();
    }

    [Fact]
    [Trait("Req", "FR-BKP-15")]
    public async Task Off_server_folders_need_the_encryption_key_first()
    {
        await using var s = await BackupTestScope.CreateAsync();

        (await s.WithAsync(sp => sp.GetRequiredService<BackupScheduleService>().SaveAsync(null, Edit(s.Nas), Ct)))
            .Error.ShouldNotBeNull().ShouldContain("encrypted");
        (await s.WithAsync(sp => sp.GetRequiredService<BackupScheduleService>().SaveAsync(null, Edit(s.Data.BackupsDirectory), Ct)))
            .Error.ShouldBeNull("the built-in folder stays on the server");
    }

    [Theory]
    [InlineData("not cron", "cron")]
    [InlineData("99 2 * * *", "cron")]
    public async Task Bad_cron_is_explained(string cron, string fragment)
    {
        await using var s = await BackupTestScope.CreateAsync();
        var edit = Edit(s.Data.BackupsDirectory) with { Frequency = ScheduleFrequency.Cron, Cron = cron };

        (await s.WithAsync(sp => sp.GetRequiredService<BackupScheduleService>().SaveAsync(null, edit, Ct))).Error.ShouldNotBeNull().ShouldContain(fragment);
    }

    [Fact]
    [Trait("Req", "AC-89")]
    [Trait("Req", "FR-BKP-14")]
    public async Task A_failed_run_is_retried_with_backoff_and_succeeds_once_the_share_is_back()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero));
        await using var s = await BackupTestScope.CreateAsync(clock);
        await s.CreateKeyAsync();
        var id = (await s.WithAsync(sp => sp.GetRequiredService<BackupScheduleService>().SaveAsync(null, Edit(s.Nas), Ct))).Schedule!.Id;
        var marker = Path.Combine(s.Nas, BackupDestinationRules.MarkerFileName);
        File.Delete(marker); // the share is "unmounted"

        await s.WithAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var sch = await db.BackupSchedules.SingleAsync(x => x.Id == id, Ct);
            sch.NextRunAt = clock.GetUtcNow(); // due now
            return await db.SaveChangesAsync(Ct);
        });

        (await s.WithAsync(sp => sp.GetRequiredService<BackupScheduleService>().RunDueAsync(Ct))).ShouldBe(1);
        var after = await s.WithAsync(sp => sp.GetRequiredService<AppDbContext>().BackupSchedules.AsNoTracking().SingleAsync(x => x.Id == id, Ct));
        after.RetryAttempt.ShouldBe(1);
        after.NextRetryAt.ShouldBe(clock.GetUtcNow().AddHours(1));
        after.NextRunAt.ShouldNotBeNull().ShouldBeGreaterThan(clock.GetUtcNow());

        clock.Advance(TimeSpan.FromMinutes(59));
        (await s.WithAsync(sp => sp.GetRequiredService<BackupScheduleService>().RunDueAsync(Ct))).ShouldBe(0);

        clock.Advance(TimeSpan.FromMinutes(1));
        await File.WriteAllTextAsync(marker, "", Ct);
        (await s.WithAsync(sp => sp.GetRequiredService<BackupScheduleService>().RunDueAsync(Ct))).ShouldBe(1);
        after = await s.WithAsync(sp => sp.GetRequiredService<AppDbContext>().BackupSchedules.AsNoTracking().SingleAsync(x => x.Id == id, Ct));
        after.RetryAttempt.ShouldBe(0);
        after.NextRetryAt.ShouldBeNull();
        Directory.GetFiles(s.Nas, "backup-*").Length.ShouldBe(1);
    }

    [Fact]
    [Trait("Req", "AC-88")]
    [Trait("Req", "BR-61")]
    public async Task Retention_deletes_only_the_schedules_own_archives_and_never_other_files()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
        await using var s = await BackupTestScope.CreateAsync(clock);
        await s.CreateKeyAsync();
        var id = (await s.WithAsync(sp => sp.GetRequiredService<BackupScheduleService>()
            .SaveAsync(null, Edit(s.Nas, new RetentionPolicy(2, 0, 0, 0)), Ct))).Schedule!.Id;
        var foreign = Path.Combine(s.Nas, "backup-20200101-000000.tar.gz.rwenc");
        await File.WriteAllTextAsync(foreign, "someone else's file", Ct);
        var manual = (await s.BackupAsync(folder: s.Nas)).Record.FullPath!; // a manual run is not this schedule's

        for (var i = 0; i < 4; i++)
        {
            clock.Advance(TimeSpan.FromDays(1));
            (await s.WithAsync(sp => sp.GetRequiredService<BackupScheduleService>().RunNowAsync(id, Ct)))!.Ok.ShouldBeTrue();
        }

        File.Exists(foreign).ShouldBeTrue();
        File.Exists(manual).ShouldBeTrue();
        var own = await s.WithAsync(sp => sp.GetRequiredService<AppDbContext>().BackupRecords.AsNoTracking().Where(r => r.ScheduleId == id).ToListAsync(Ct));
        own.Count(r => r.DeletedAt == null).ShouldBe(2);
        foreach (var r in own)
        {
            File.Exists(r.FullPath!).ShouldBe(r.DeletedAt == null);
        }
    }

    [Fact]
    public async Task Runs_left_running_by_a_stopped_process_are_closed_at_start()
    {
        await using var s = await BackupTestScope.CreateAsync();
        await s.WithAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            db.BackupRecords.Add(new BackupRecord { Kind = BackupKind.Manual, Status = BackupStatus.Running, DestinationFolder = s.Nas, StartedBy = "x" });
            return await db.SaveChangesAsync(Ct);
        });

        (await s.WithAsync(sp => sp.GetRequiredService<BackupScheduleService>().CloseInterruptedAsync(Ct))).ShouldBe(1);
    }

    [Fact]
    [Trait("Req", "BR-61")]
    public async Task Admins_cannot_delete_recent_safety_backups()
    {
        await using var s = await BackupTestScope.CreateAsync();
        var safety = (await s.BackupAsync(BackupKind.PreUpgrade)).Record.Id;
        var manual = (await s.BackupAsync()).Record.Id;

        (await s.WithAsync(sp => sp.GetRequiredService<BackupService>().DeleteArchiveAsync(safety, Ct))).ShouldNotBeNull().ShouldContain("30 days");
        (await s.WithAsync(sp => sp.GetRequiredService<BackupService>().DeleteArchiveAsync(manual, Ct))).ShouldBeNull();
        (await s.WithAsync(sp => sp.GetRequiredService<BackupService>().DeleteArchiveAsync(manual, Ct))).ShouldNotBeNull();
    }
}
