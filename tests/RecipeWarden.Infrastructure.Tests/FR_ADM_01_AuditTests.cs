using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Health;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Audit;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Health;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Infrastructure.Setup;
using RecipeWarden.Tests.Shared;

namespace RecipeWarden.Infrastructure.Tests;

[Trait("Req", "FR-ADM-01")]
[Trait("Req", "AC-74")]
public sealed class FR_ADM_01_AuditTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_update_and_delete_are_audited_with_actor_entity_and_ip_and_no_values()
    {
        await using var s = await BackupTestScope.CreateAsync();
        int id;
        using (AuditScope.As(new AuditActorInfo("u-1", "cook@example.com", "192.0.2.10")))
        {
            id = await s.AddRecipeAsync("Chili");
            await s.WithAsync(async sp =>
            {
                var db = sp.GetRequiredService<AppDbContext>();
                var r = await db.Recipes.SingleAsync(x => x.Id == id, Ct);
                r.Description = "secret family twist";
                return await db.SaveChangesAsync(Ct);
            });
            await s.WithAsync(async sp =>
            {
                await sp.GetRequiredService<RecipeService>().DeleteAsync(id, "u-1", Ct);
                return 0;
            });
        }

        var entries = await s.WithAsync(sp => sp.GetRequiredService<AppDbContext>().AuditEntries.AsNoTracking().OrderBy(e => e.Id).ToListAsync(Ct));
        var create = entries.First(e => e.Action == "Recipe.Create");
        create.EntityId.ShouldBe(id.ToString(CultureInfo.InvariantCulture));
        create.UserId.ShouldBe("u-1");
        create.UserName.ShouldBe("cook@example.com");
        create.Ip.ShouldBe("192.0.2.10");
        create.DetailsJson.ShouldNotBeNull().ShouldContain("Chili");

        var update = entries.First(e => e.Action == "Recipe.Update" && e.DetailsJson!.Contains("Description", StringComparison.Ordinal));
        update.DetailsJson!.ShouldNotContain("secret family twist", customMessage: "values are never stored");
        entries.ShouldAllBe(e => e.At != default);
    }

    [Fact]
    public async Task Seed_data_is_not_audited_and_audit_rows_do_not_audit_themselves()
    {
        await using var s = await BackupTestScope.CreateAsync();
        var actions = await s.WithAsync(sp => sp.GetRequiredService<AppDbContext>().AuditEntries.Select(e => e.Action).ToListAsync(Ct));
        actions.ShouldNotContain(a => a.StartsWith("Food.", StringComparison.Ordinal) || a.StartsWith("NutrientRef", StringComparison.Ordinal));
        actions.ShouldNotContain(a => a.StartsWith("AuditEntry", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "SEC-72")]
    public async Task Role_changes_are_audited_and_password_hashes_never_appear()
    {
        await using var s = await BackupTestScope.CreateAsync();
        await s.WithAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<AppUser>>();
            var u = new AppUser { UserName = "ed@example.com", Email = "ed@example.com" };
            (await users.CreateAsync(u, "a very long password")).Succeeded.ShouldBeTrue();
            (await users.AddToRoleAsync(u, AppRoles.Editor)).Succeeded.ShouldBeTrue();
            return 0;
        });

        var entries = await s.WithAsync(sp => sp.GetRequiredService<AppDbContext>().AuditEntries.AsNoTracking().ToListAsync(Ct));
        entries.ShouldContain(e => e.Action == "User.Create");
        entries.ShouldContain(e => e.Action == "UserRole.Create");
        entries.ShouldAllBe(e => e.DetailsJson == null || !e.DetailsJson.Contains("AQAAAA", StringComparison.Ordinal));
        entries.ShouldAllBe(e => e.UserName == "system");
    }

    [Fact]
    public async Task Explicit_events_and_queries()
    {
        await using var s = await BackupTestScope.CreateAsync();
        await s.WithAsync(async sp =>
        {
            await sp.Audit().RecordAsync("Account.SignIn", "User", "u-1", new { ok = true }, Ct);
            await sp.Audit().RecordAsAsync(new AuditActorInfo(null, "anonymous", "198.51.100.7"), "Account.SignInFailed", null, null, new { email = "x@example.com" }, Ct);
            return 0;
        });

        var page = await s.WithAsync(sp => sp.Audit().QueryAsync(new AuditQuery(Action: "Account."), Ct));
        page.Entries.Select(e => e.Action).ShouldBe(["Account.SignInFailed", "Account.SignIn"]);
        (await s.WithAsync(sp => sp.Audit().QueryAsync(new AuditQuery(User: "anonymous"), Ct))).Entries.ShouldHaveSingleItem();
        (await s.WithAsync(sp => sp.Audit().QueryAsync(new AuditQuery(Entity: "User"), Ct))).Entries.ShouldHaveSingleItem();
        (await s.WithAsync(sp => sp.Audit().QueryAsync(new AuditQuery(From: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))), Ct))).Entries.ShouldBeEmpty();
        (await s.WithAsync(sp => sp.Audit().QueryAsync(new AuditQuery(To: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))), Ct))).Entries.Count.ShouldBeGreaterThan(1);
        (await s.WithAsync(sp => sp.Audit().EntityTypesAsync(Ct))).ShouldContain("User");
        (await s.WithAsync(sp => sp.Audit().RecentAsync(1, Ct))).ShouldHaveSingleItem().Action.ShouldBe("Account.SignInFailed");
    }
}

[Trait("Req", "FR-OPS-02")]
public sealed class FR_OPS_02_HealthServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeVolumes : IVolumeProbe
    {
        public long Total { get; set; } = 100L * 1024 * 1024 * 1024;
        public long Free { get; set; } = 50L * 1024 * 1024 * 1024;

        public (long Total, long Free) Measure(string path) => (Total, Free);
    }

    [Fact]
    [Trait("Req", "AC-98")]
    public async Task Report_shows_database_and_space_figures_that_match_the_files()
    {
        await using var s = await BackupTestScope.CreateAsync();
        Directory.CreateDirectory(s.Data.MediaDirectory);
        await File.WriteAllBytesAsync(Path.Combine(s.Data.MediaDirectory, "a.webp"), new byte[12345], Ct);

        var report = await s.WithAsync(sp => sp.Health().EvaluateAsync(store: true, Ct));

        report.Database.FileBytes.ShouldBe(new FileInfo(s.Data.DatabasePath).Length);
        report.Database.PageCount.ShouldBeGreaterThan(0);
        report.Database.SchemaVersion.ShouldBe(await s.WithAsync(sp => Task.FromResult(sp.GetRequiredService<AppDbContext>().Database.GetMigrations().Last())));
        report.Space.Media.ShouldBe(12345);
        report.Checks.Select(c => c.Name).ShouldContain("Database");
        report.Checks.Select(c => c.Name).ShouldContain("Backups");
        report.Volumes.ShouldContain(v => v.Label == "data");
        (await s.WithAsync(sp => sp.GetRequiredService<AppDbContext>().HealthCheckResults.CountAsync(Ct))).ShouldBeGreaterThan(0);
    }

    [Fact]
    [Trait("Req", "AC-98")]
    public void Volume_figures_match_df_within_one_percent()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var dir = Path.GetTempPath();
        var (total, free) = new DriveInfoVolumeProbe().Measure(dir);
        var psi = new ProcessStartInfo("df", ["-B1", "--output=size,avail", dir]) { RedirectStandardOutput = true };
        using var p = Process.Start(psi)!;
        var lines = p.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        p.WaitForExit();
        var parts = lines[^1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var dfTotal = long.Parse(parts[0], CultureInfo.InvariantCulture);
        var dfFree = long.Parse(parts[1], CultureInfo.InvariantCulture);

        Math.Abs(total - dfTotal).ShouldBeLessThanOrEqualTo(dfTotal / 100);
        Math.Abs(free - dfFree).ShouldBeLessThanOrEqualTo(dfTotal / 100);
        new DriveInfoVolumeProbe().Measure(Path.Combine(dir, "does", "not", "exist")).Total.ShouldBe(total);
    }

    [Fact]
    [Trait("Req", "AC-99")]
    public async Task Simulated_low_space_raises_warning_then_critical_with_a_recommendation()
    {
        var volumes = new FakeVolumes();
        await using var s = await BackupTestScope.CreateAsync(configure: sc => sc.AddSingleton<IVolumeProbe>(volumes));
        await s.BackupAsync(); // a recent backup, so only the disk drives the status

        volumes.Free = volumes.Total * 15 / 100;
        var warning = await s.WithAsync(sp => sp.Health().EvaluateAsync(store: false, Ct));
        warning.Status.ShouldBe(HealthStatus.Warning);
        warning.Recommendation.ShouldNotBeNull();
        s.Services.GetRequiredService<HealthState>().Banner.ShouldNotBeNull().ShouldStartWith("Warning");

        volumes.Free = volumes.Total * 8 / 100;
        (await s.WithAsync(sp => sp.Health().EvaluateAsync(store: false, Ct))).Status.ShouldBe(HealthStatus.Critical);
        s.Services.GetRequiredService<HealthState>().Status.ShouldBe(HealthStatus.Critical);
    }

    [Fact]
    [Trait("Req", "FR-OPS-03")]
    public async Task One_snapshot_a_day()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 23, 1, 0, 0, TimeSpan.Zero));
        await using var s = await BackupTestScope.CreateAsync(clock);

        (await s.WithAsync(sp => sp.Health().TakeDailySnapshotAsync(Ct))).ShouldBeTrue();
        (await s.WithAsync(sp => sp.Health().TakeDailySnapshotAsync(Ct))).ShouldBeFalse();
        clock.Advance(TimeSpan.FromDays(1));
        (await s.WithAsync(sp => sp.Health().TakeDailySnapshotAsync(Ct))).ShouldBeTrue();
        (await s.WithAsync(sp => sp.GetRequiredService<AppDbContext>().StorageSnapshots.CountAsync(Ct))).ShouldBe(2);
    }

    [Fact]
    [Trait("Req", "FR-OPS-04")]
    [Trait("Req", "BR-83")]
    public async Task Integrity_check_and_compact()
    {
        await using var s = await BackupTestScope.CreateAsync();
        (await s.WithAsync(sp => sp.Health().ChecksDueAsync(Ct))).Full.ShouldBeTrue();
        (await s.WithAsync(sp => sp.Health().RunIntegrityCheckAsync(full: true, Ct))).ShouldBe("ok");
        (await s.WithAsync(sp => sp.Health().ChecksDueAsync(Ct))).ShouldBe((false, false));
        var report = await s.WithAsync(sp => sp.Health().EvaluateAsync(store: false, Ct));
        report.LastIntegrityOk.ShouldBe(true);
        (await s.WithAsync(sp => sp.Health().CompactAsync(Ct))).ShouldBeGreaterThanOrEqualTo(0);
    }
}

[Trait("Req", "SEC-15")]
[Trait("Req", "AC-84")]
public sealed class SEC_15_SetupTokenTests
{
    [Fact]
    public async Task Token_works_once_setup_is_open_and_never_after_completion()
    {
        await using var s = await BackupTestScope.CreateAsync();
        await using var scope = s.Services.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<SetupService>();

        (await setup.IsAvailableAsync()).ShouldBeFalse("no token issued yet");
        var token = (await setup.IssueTokenAsync()).ShouldNotBeNull();
        token.Length.ShouldBeGreaterThanOrEqualTo(30);
        (await setup.IsAvailableAsync()).ShouldBeTrue();
        (await setup.CheckTokenAsync(token)).ShouldBeTrue();
        (await setup.CheckTokenAsync(token + "x")).ShouldBeFalse();
        (await setup.CheckTokenAsync(null)).ShouldBeFalse();
        File.ReadAllText(Path.Combine(s.Data.SetupDirectory, "token.json")).ShouldNotContain(token, customMessage: "only the hash is stored");

        setup.Complete("test");
        (await setup.IsAvailableAsync()).ShouldBeFalse();
        (await setup.CheckTokenAsync(token)).ShouldBeFalse();
        (await setup.IssueTokenAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task No_token_once_an_admin_exists()
    {
        await using var s = await BackupTestScope.CreateAsync();
        await using var scope = s.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var admin = new AppUser { UserName = "a@example.com", Email = "a@example.com" };
        (await users.CreateAsync(admin, "a very long password")).Succeeded.ShouldBeTrue();
        (await users.AddToRoleAsync(admin, AppRoles.Admin)).Succeeded.ShouldBeTrue();

        (await scope.ServiceProvider.GetRequiredService<SetupService>().IssueTokenAsync()).ShouldBeNull();
    }
}
