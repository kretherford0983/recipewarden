using RecipeWarden.Cli.ApplyConfig;
using RecipeWarden.Core.Backup;

namespace RecipeWarden.Cli.Tests;

/// <summary>FR-BKP-09 / AC-86 (PennyWarden AC-BKP-001 to 004): apply-config behaviour.</summary>
[Trait("Req", "FR-BKP-09")]
[Trait("Req", "AC-86")]
public sealed class FR_BKP_09_ApplyConfigTests
{
    private readonly FakeFileSystem fs = new();
    private readonly FakeSystemd systemd = new();
    private readonly StringWriter output = new();
    private readonly StringWriter error = new();

    private ApplyConfigRunner Runner(params bool[] health) => new(fs, systemd, new FakeHealth(health), output, error);

    private static ApplyConfigRequest Request(string?[] folders, bool dryRun = false, bool noRestart = false, bool privileged = true) =>
        new(folders, dryRun, noRestart, privileged, "alice");

    private const string Previous =
        "# Written by \"recipewarden apply-config\" from Backup:AllowedFolders - don't edit\n[Service]\nReadWritePaths=\"-/var/lib/recipewarden/backups\"\n";

    [Theory]
    [InlineData("relative/path")]
    [InlineData("/home/alice/backups")]
    [InlineData("/root/backups")]
    [InlineData("/run/user/1000/backups")]
    public async Task Invalid_folder_fails_and_changes_nothing(string bad)
    {
        fs.Files[BackupFoldersDropIn.FilePath] = Previous;

        var exit = await Runner().RunAsync(Request(["/mnt/nas/recipewarden", bad]), TestContext.Current.CancellationToken);

        exit.ShouldBe(ApplyConfigRunner.ExitInvalidConfig);
        fs.Writes.ShouldBeEmpty();
        systemd.Calls.ShouldBeEmpty();
        fs.Files[BackupFoldersDropIn.FilePath].ShouldBe(Previous);
        error.ToString().ShouldContain("Nothing was changed");
    }

    [Fact]
    public async Task Missing_folder_is_a_warning_and_the_service_still_restarts()
    {
        fs.Paths["/mnt/nas/recipewarden"] = PathKind.Missing;

        var exit = await Runner(true).RunAsync(Request(["/mnt/nas/recipewarden"]), TestContext.Current.CancellationToken);

        exit.ShouldBe(ApplyConfigRunner.ExitOk);
        output.ToString().ShouldContain("warning: /mnt/nas/recipewarden");
        systemd.Calls.ShouldBe(["daemon-reload", "restart recipewarden.service"]);
    }

    [Fact]
    public async Task Drop_in_has_exactly_one_line_per_folder()
    {
        var exit = await Runner(true).RunAsync(
            Request(["/var/lib/recipewarden/backups", "/mnt/nas/recipewarden/", "/mnt/nas/recipewarden"]), TestContext.Current.CancellationToken);

        exit.ShouldBe(ApplyConfigRunner.ExitOk);
        var lines = fs.Files[BackupFoldersDropIn.FilePath].Split('\n').Where(l => l.StartsWith("ReadWritePaths=", StringComparison.Ordinal)).ToList();
        lines.ShouldBe(["ReadWritePaths=\"-/var/lib/recipewarden/backups\"", "ReadWritePaths=\"-/mnt/nas/recipewarden\""]);
    }

    [Fact]
    public async Task Empty_list_removes_the_drop_in()
    {
        fs.Files[BackupFoldersDropIn.FilePath] = Previous;

        var exit = await Runner(true).RunAsync(Request([]), TestContext.Current.CancellationToken);

        exit.ShouldBe(ApplyConfigRunner.ExitOk);
        fs.Files.ContainsKey(BackupFoldersDropIn.FilePath).ShouldBeFalse();
        output.ToString().ShouldContain("- /var/lib/recipewarden/backups");
    }

    [Fact]
    public async Task Health_failure_restores_the_previous_drop_in()
    {
        fs.Files[BackupFoldersDropIn.FilePath] = Previous;

        var exit = await Runner(false).RunAsync(Request(["/var/lib/recipewarden/backups", "/mnt/nas/recipewarden"]), TestContext.Current.CancellationToken);

        exit.ShouldBe(ApplyConfigRunner.ExitHealthFailedRolledBack);
        fs.Files[BackupFoldersDropIn.FilePath].ShouldBe(Previous);
        systemd.Calls.ShouldBe(["daemon-reload", "restart recipewarden.service", "daemon-reload", "restart recipewarden.service"]);
        error.ToString().ShouldContain("did not become healthy");
    }

    [Fact]
    public async Task Health_failure_with_no_previous_drop_in_removes_the_new_one()
    {
        var exit = await Runner(false).RunAsync(Request(["/mnt/nas/recipewarden"]), TestContext.Current.CancellationToken);

        exit.ShouldBe(ApplyConfigRunner.ExitHealthFailedRolledBack);
        fs.Files.ContainsKey(BackupFoldersDropIn.FilePath).ShouldBeFalse();
    }

    [Fact]
    public async Task Override_conf_is_never_touched()
    {
        fs.Files[BackupFoldersDropIn.OverrideFilePath] = "[Service]\nEnvironment=X=1\n";

        await Runner(false).RunAsync(Request(["/mnt/nas/recipewarden"]), TestContext.Current.CancellationToken);
        await Runner(true).RunAsync(Request([]), TestContext.Current.CancellationToken);

        fs.Writes.ShouldAllBe(w => !w.Contains("override.conf", StringComparison.Ordinal));
        fs.Files[BackupFoldersDropIn.OverrideFilePath].ShouldBe("[Service]\nEnvironment=X=1\n");
    }

    [Fact]
    public async Task Dry_run_changes_nothing()
    {
        var exit = await Runner(true).RunAsync(Request(["/mnt/nas/recipewarden"], dryRun: true, privileged: false), TestContext.Current.CancellationToken);

        exit.ShouldBe(ApplyConfigRunner.ExitOk);
        fs.Writes.ShouldBeEmpty();
        systemd.Calls.ShouldBeEmpty();
        output.ToString().ShouldContain("--dry-run: would write");
        output.ToString().ShouldContain("+ /mnt/nas/recipewarden");
    }

    [Fact]
    public async Task Dry_run_reports_removal_for_an_empty_list()
    {
        fs.Files[BackupFoldersDropIn.FilePath] = Previous;
        await Runner().RunAsync(Request([], dryRun: true), TestContext.Current.CancellationToken);
        output.ToString().ShouldContain("--dry-run: would remove");
        fs.Writes.ShouldBeEmpty();
    }

    [Fact]
    public async Task No_restart_writes_and_reloads_but_does_not_restart()
    {
        var exit = await Runner().RunAsync(Request(["/mnt/nas/recipewarden"], noRestart: true), TestContext.Current.CancellationToken);

        exit.ShouldBe(ApplyConfigRunner.ExitOk);
        fs.Files.ContainsKey(BackupFoldersDropIn.FilePath).ShouldBeTrue();
        systemd.Calls.ShouldBe(["daemon-reload"]);
    }

    [Fact]
    public async Task Unchanged_config_does_nothing()
    {
        fs.Files[BackupFoldersDropIn.FilePath] = Previous;

        var exit = await Runner().RunAsync(Request(["/var/lib/recipewarden/backups"]), TestContext.Current.CancellationToken);

        exit.ShouldBe(ApplyConfigRunner.ExitOk);
        fs.Writes.ShouldBeEmpty();
        systemd.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Requires_root_to_apply()
    {
        var exit = await Runner().RunAsync(Request(["/mnt/nas/recipewarden"], privileged: false), TestContext.Current.CancellationToken);

        exit.ShouldBe(ApplyConfigRunner.ExitNotPrivileged);
        fs.Writes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Daemon_reload_failure_restores_the_previous_drop_in()
    {
        fs.Files[BackupFoldersDropIn.FilePath] = Previous;
        systemd.ReloadSucceeds = false;

        var exit = await Runner().RunAsync(Request(["/mnt/nas/recipewarden"]), TestContext.Current.CancellationToken);

        exit.ShouldBe(ApplyConfigRunner.ExitSystemdFailed);
        fs.Files[BackupFoldersDropIn.FilePath].ShouldBe(Previous);
    }

    [Fact]
    public async Task Audits_before_and_after_folders_and_the_os_user()
    {
        fs.Files[BackupFoldersDropIn.FilePath] = Previous;

        await Runner(true).RunAsync(Request(["/mnt/nas/recipewarden"]), TestContext.Current.CancellationToken);

        output.ToString().ShouldContain("audit: apply-config by alice: before=[/var/lib/recipewarden/backups] after=[/mnt/nas/recipewarden]");
    }
}
