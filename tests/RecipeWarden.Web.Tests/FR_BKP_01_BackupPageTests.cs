using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Backup;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Backup;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Web.Tests;

/// <summary>W7 through the pages: back up now, the key and recovery kit, schedules, Test, archives and restore.</summary>
[Trait("Req", "FR-BKP-01")]
[Trait("Req", "FR-BKP-04")]
public sealed partial class FR_BKP_01_BackupPageTests : IClassFixture<FR_BKP_01_BackupPageTests.Factory>
{
    private readonly Factory factory;

    public FR_BKP_01_BackupPageTests(Factory factory) => this.factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A host with a second allowed folder ("the NAS") holding its marker file.</summary>
    public sealed class Factory : IDisposable
    {
        public Factory()
        {
            Nas = Path.Combine(Path.GetTempPath(), "rw-web-nas-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Nas);
            File.WriteAllText(Path.Combine(Nas, BackupDestinationRules.MarkerFileName), "");
            Host = new RecipeWardenFactory();
            Host.Settings["Backup:AllowedFolders:0"] = Nas;
        }

        public string Nas { get; }

        public RecipeWardenFactory Host { get; }

        public void Dispose()
        {
            Host.Dispose();
            try
            {
                Directory.Delete(Nas, recursive: true);
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }

    private async Task<HttpClient> AdminAsync(string name = "backup-admin@example.com") => await factory.Host.SignInAsync(name, AppRoles.Admin);

    private async Task<List<BackupRecord>> RecordsAsync()
    {
        await using var scope = factory.Host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().BackupRecords.AsNoTracking().OrderBy(r => r.Id).ToListAsync(Ct);
    }

    private async Task<List<string>> TitlesAsync()
    {
        await using var scope = factory.Host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Recipes.Select(r => r.Title).ToListAsync(Ct);
    }

    [Fact]
    [Trait("Req", "AC-80")]
    public async Task Back_up_now_lists_the_archive_with_size_and_a_download()
    {
        using var admin = await AdminAsync();
        var response = await admin.PostFormAsync("/admin/backups?handler=BackupNow", ("folder", factory.Host.DataDirectory + "/backups"));
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect, await response.TextAsync());

        var record = (await RecordsAsync()).Last(r => r.Kind == BackupKind.Manual);
        record.Status.ShouldBe(BackupStatus.Succeeded);
        var html = await admin.GetStringAsync("/admin/backups", Ct);
        html.ShouldContain(record.FileName!);
        html.ShouldContain($"handler=Download&amp;id={record.Id}");

        var download = await admin.GetAsync($"/admin/backups?handler=Download&id={record.Id}", Ct);
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync(Ct)).Length.ShouldBe((int)record.Bytes);
        (await admin.GetAsync("/admin/backups?handler=Download&id=99999", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Req", "FR-BKP-15")]
    [Trait("Req", "SEC-75")]
    public async Task Creating_the_key_downloads_the_kit_once_and_the_server_keeps_only_the_public_key()
    {
        using var admin = await AdminAsync("key-admin@example.com");
        var mismatch = await admin.PostFormAsync("/admin/backups?handler=CreateKey", ("passphrase", "correct horse battery"), ("confirm", "something else!!"));
        mismatch.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var response = await admin.PostFormAsync("/admin/backups?handler=CreateKey", ("passphrase", "correct horse battery"), ("confirm", "correct horse battery"),
            ("replace", "true"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.FileName!.ShouldContain("recipewarden-recovery-kit-");
        var kit = await response.TextAsync();
        kit.ShouldContain(BackupCrypto.KitBegin);

        await using var scope = factory.Host.Services.CreateAsyncScope();
        var settings = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AppSettings.AsNoTracking().ToListAsync(Ct);
        settings.ShouldAllBe(s => !s.Value.Contains("PRIVATE KEY", StringComparison.Ordinal));
        foreach (var file in Directory.EnumerateFiles(factory.Host.DataDirectory, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".db", StringComparison.Ordinal)
                     && !f.EndsWith("-wal", StringComparison.Ordinal) && !f.EndsWith("-shm", StringComparison.Ordinal) && !f.Contains("backup-", StringComparison.Ordinal)))
        {
            (await File.ReadAllTextAsync(file, Ct)).ShouldNotContain("ENCRYPTED PRIVATE KEY", customMessage: file);
        }
    }

    [Fact]
    [Trait("Req", "AC-87")]
    [Trait("Req", "SEC-74")]
    [Trait("Req", "FR-BKP-05")]
    public async Task Schedules_take_only_allowed_folders_and_changes_are_audited()
    {
        using var admin = await AdminAsync("sched-admin@example.com");
        await admin.PostFormAsync("/admin/backups?handler=CreateKey", ("passphrase", "correct horse battery"), ("confirm", "correct horse battery"), ("replace", "true"));

        var refused = await admin.PostFormAsync("/admin/backups?handler=SaveSchedule", ("name", "Evil"), ("frequency", "Daily"), ("time", "02:30"),
            ("folder", "/etc"), ("keepLast", "3"), ("keepDaily", "7"), ("keepWeekly", "4"), ("keepMonthly", "6"), ("enabled", "true"));
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.TextAsync()).ShouldContain("allowed backup folders");

        var saved = await admin.PostFormAsync("/admin/backups?handler=SaveSchedule", ("name", "Nightly to the NAS"), ("frequency", "Daily"), ("time", "03:15"),
            ("folder", factory.Nas + "/"), ("keepLast", "3"), ("keepDaily", "7"), ("keepWeekly", "4"), ("keepMonthly", "6"), ("enabled", "true"));
        saved.StatusCode.ShouldBe(HttpStatusCode.Redirect, await saved.TextAsync());

        await using var scope = factory.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var schedule = await db.BackupSchedules.AsNoTracking().SingleAsync(s => s.Name == "Nightly to the NAS", Ct);
        schedule.DestinationFolder.ShouldBe(factory.Nas);
        schedule.Cron.ShouldBe("15 3 * * *");
        (await db.AuditEntries.AsNoTracking().AnyAsync(a => a.Action == "BackupSchedule.Create" && a.UserName == "sched-admin@example.com", Ct)).ShouldBeTrue();

        var run = await admin.PostFormAsync("/admin/backups?handler=RunSchedule", ("id", schedule.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        run.StatusCode.ShouldBe(HttpStatusCode.Redirect, await run.TextAsync());
        Directory.GetFiles(factory.Nas, "backup-*.rwenc").ShouldNotBeEmpty();
        (await admin.GetStringAsync("/admin/backups", Ct)).ShouldContain("Daily at 03:15");
    }

    [Fact]
    [Trait("Req", "FR-BKP-11")]
    [Trait("Req", "AC-87")]
    public async Task Test_button_reports_success_and_refuses_unlisted_folders()
    {
        using var admin = await AdminAsync("test-admin@example.com");
        var ok = await admin.PostFormAsync("/admin/backups?handler=Test", ("folder", factory.Nas));
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ok.TextAsync()).ShouldContain("The folder is writable.");
        Directory.GetFiles(factory.Nas, BackupDestinationRules.ProbePrefix + "*").ShouldBeEmpty();

        (await admin.PostFormAsync("/admin/backups?handler=Test", ("folder", "/tmp"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Req", "AC-82")]
    [Trait("Req", "W7")]
    public async Task Restore_from_a_listed_archive_needs_the_typed_word_and_brings_the_data_back()
    {
        using var admin = await AdminAsync("restore-admin@example.com");
        var kitResponse = await admin.PostFormAsync("/admin/backups?handler=CreateKey", ("passphrase", "correct horse battery"), ("confirm", "correct horse battery"),
            ("replace", "true"));
        var kit = await kitResponse.Content.ReadAsByteArrayAsync(Ct);
        await admin.AddRecipeAsync("Before Backup Soup");
        (await admin.PostFormAsync("/admin/backups?handler=BackupNow", ("folder", factory.Host.DataDirectory + "/backups"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var record = (await RecordsAsync()).Last();
        record.Encrypted.ShouldBeTrue();
        await admin.AddRecipeAsync("After Backup Stew");

        var noKit = await admin.PostFormAsync("/admin/backups?handler=PrepareRestore", ("recordId", record.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        noKit.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var wrongPass = await admin.PostFilesAsync("/admin/backups?handler=PrepareRestore", [("kitFile", "kit.txt", kit)],
            ("recordId", record.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("passphrase", "wrong horse battery"));
        wrongPass.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await wrongPass.TextAsync()).ShouldContain("recovery kit or passphrase is wrong");

        var report = await admin.PostFilesAsync("/admin/backups?handler=PrepareRestore", [("kitFile", "kit.txt", kit)],
            ("recordId", record.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("passphrase", "correct horse battery"));
        report.StatusCode.ShouldBe(HttpStatusCode.OK, await report.TextAsync());
        var html = await report.TextAsync();
        html.ShouldContain("Ready to restore");
        var id = RestoreId().Match(html).Groups[1].Value;
        id.Length.ShouldBe(32);
        (await TitlesAsync()).ShouldContain("After Backup Stew", "checking an archive changes nothing");

        var wrong = await admin.PostFormAsync("/admin/backups?handler=ConfirmRestore", ("restoreId", id), ("confirmation", "restore"));
        wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TitlesAsync()).ShouldContain("After Backup Stew");

        var done = await admin.PostFormAsync("/admin/backups?handler=ConfirmRestore", ("restoreId", id), ("confirmation", "RESTORE"));
        done.StatusCode.ShouldBe(HttpStatusCode.Redirect, await done.TextAsync());
        done.Headers.Location!.ToString().ShouldBe("/admin/backups?done=restored");
        var titles = await TitlesAsync();
        titles.ShouldContain("Before Backup Soup");
        titles.ShouldNotContain("After Backup Stew");
        (await RecordsAsync()).ShouldContain(r => r.Kind == BackupKind.PreRestore);
        (await admin.GetAsync("/", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK, "the site is back in service");

        (await admin.PostFormAsync("/admin/backups?handler=ConfirmRestore", ("restoreId", id), ("confirmation", "RESTORE")))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest, "a preparation is used once");
    }

    [Fact]
    [Trait("Req", "AC-82")]
    [Trait("Req", "SEC-54")]
    public async Task Uploaded_files_that_are_not_backups_are_refused_before_anything_changes()
    {
        using var admin = await AdminAsync("upload-admin@example.com");
        var notGzip = await admin.PostFilesAsync("/admin/backups?handler=PrepareRestore", [("archive", "backup.tar.gz", "hello"u8.ToArray())]);
        notGzip.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await notGzip.TextAsync()).ShouldContain(ArchiveUploadRules.WrongBackupType);

        var exe = await admin.PostFilesAsync("/admin/backups?handler=PrepareRestore", [("archive", "backup.exe", [0x1F, 0x8B, 8, 0])]);
        exe.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var work = Path.Combine(factory.Host.DataDirectory, "tmp");
        (Directory.Exists(work) ? Directory.EnumerateFiles(work, "upload-*") : []).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Req", "BR-63")]
    public async Task During_a_restore_every_other_request_gets_the_maintenance_page()
    {
        using var client = factory.Host.CreateHttpsClient();
        var maintenance = factory.Host.Services.GetRequiredService<MaintenanceMode>();
        using (maintenance.Enter("test"))
        {
            var page = await client.GetAsync("/", Ct);
            page.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            (await page.TextAsync()).ShouldContain("restoring a backup");
            (await client.GetAsync("/health", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
            using var post = new HttpRequestMessage(HttpMethod.Post, "/account/login");
            (await client.SendAsync(post, Ct)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        }

        (await client.GetAsync("/", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Req", "AC-71")]
    public async Task Editors_cannot_back_up_restore_or_download()
    {
        using var editor = await factory.Host.SignInAsync("backup-editor@example.com", AppRoles.Editor);
        (await editor.GetAsync("/admin/backups?handler=Download&id=1", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var token = await RecipeWardenFactory.GetAntiforgeryTokenAsync(editor, "/manage");
        var post = await editor.PostAsync("/admin/backups?handler=BackupNow", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }), Ct);
        post.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [GeneratedRegex("name=\"restoreId\" value=\"([0-9a-f]+)\"")]
    private static partial Regex RestoreId();
}
