using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using RecipeWarden.Cli.Admin;
using RecipeWarden.Cli.Operations;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Cli.Tests;

/// <summary>FR-BKP-07, AC-85, SEC-15: the backup, restore, setup-token and health commands.</summary>
[Trait("Req", "FR-BKP-07")]
public sealed class FR_BKP_07_CliOperationsTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("rw-cli-ops-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Data:Directory"] = dir, ["Health:LogDirectory"] = Path.Combine(dir, "logs") })
        .Build();

    private async Task<int> UserCountAsync()
    {
        await using var c = new SqliteConnection($"Data Source={new DataOptions { Directory = dir }.DatabasePath};Pooling=False");
        await c.OpenAsync(Ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM AspNetUsers;";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(Ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    [Trait("Req", "FR-BKP-01")]
    [Trait("Req", "AC-85")]
    public async Task Backup_then_check_and_apply_restore_brings_back_the_earlier_accounts()
    {
        await AdminCreateCommand.RunAsync(Config(), "first@example.com", "correct horse battery", new StringWriter(), new StringWriter(), Ct);
        var output = new StringWriter();
        (await OperationsCommands.BackupAsync(Config(), null, null, output, new StringWriter(), Ct)).ShouldBe(0);
        var archive = output.ToString().Split('\n')[0].Trim();
        File.Exists(archive).ShouldBeTrue();

        await AdminCreateCommand.RunAsync(Config(), "second@example.com", "correct horse battery", new StringWriter(), new StringWriter(), Ct);
        (await UserCountAsync()).ShouldBe(2);

        var check = new StringWriter();
        (await OperationsCommands.RestoreOfflineAsync(Config(), archive, null, null, apply: false, preRestore: true, "test", check, new StringWriter(), Ct)).ShouldBe(0);
        check.ToString().ShouldContain("Archive OK");
        check.ToString().ShouldContain("1 accounts");
        (await UserCountAsync()).ShouldBe(2, "--check changes nothing");

        var applied = new StringWriter();
        (await OperationsCommands.RestoreOfflineAsync(Config(), archive, null, null, apply: true, preRestore: true, "test", applied, new StringWriter(), Ct)).ShouldBe(0);
        applied.ToString().ShouldContain("Restored and migrated.");
        (await UserCountAsync()).ShouldBe(1);
        Directory.GetFiles(Path.Combine(dir, "backups"), "backup-*").Length.ShouldBe(2, "the archive and the pre-restore backup");
    }

    [Fact]
    [Trait("Req", "SEC-54")]
    public async Task Restore_refuses_a_missing_or_bad_file_and_changes_nothing()
    {
        await AdminCreateCommand.RunAsync(Config(), "a@example.com", "correct horse battery", new StringWriter(), new StringWriter(), Ct);
        var error = new StringWriter();
        (await OperationsCommands.RestoreOfflineAsync(Config(), Path.Combine(dir, "nope.tar.gz"), null, null, true, true, "test", new StringWriter(), error, Ct)).ShouldBe(1);

        var bad = Path.Combine(dir, "bad.tar.gz");
        await File.WriteAllTextAsync(bad, "not an archive", Ct);
        (await OperationsCommands.RestoreOfflineAsync(Config(), bad, null, null, true, true, "test", new StringWriter(), error, Ct)).ShouldBe(1);
        error.ToString().ShouldContain("nothing was changed");
        (await UserCountAsync()).ShouldBe(1);

        (await OperationsCommands.RestoreOfflineAsync(Config(), bad, Path.Combine(dir, "missing-kit.txt"), "x", true, true, "test", new StringWriter(), error, Ct)).ShouldBe(1);
    }

    [Fact]
    [Trait("Req", "FR-BKP-08")]
    public async Task Backup_refuses_folders_that_are_not_allowed_and_bad_kinds()
    {
        var error = new StringWriter();
        (await OperationsCommands.BackupAsync(Config(), "/etc", null, new StringWriter(), error, Ct)).ShouldBe(1);
        error.ToString().ShouldContain("not in Backup:AllowedFolders");
        (await OperationsCommands.BackupAsync(Config(), null, "weekly", new StringWriter(), error, Ct)).ShouldBe(1);
        (await OperationsCommands.BackupAsync(Config(), null, "pre-upgrade", new StringWriter(), new StringWriter(), Ct)).ShouldBe(0);
    }

    [Fact]
    [Trait("Req", "SEC-15")]
    public async Task Setup_token_only_before_the_first_admin()
    {
        var output = new StringWriter();
        (await OperationsCommands.SetupTokenAsync(Config(), output, new StringWriter(), Ct)).ShouldBe(0);
        output.ToString().Trim().Length.ShouldBeGreaterThanOrEqualTo(30);

        await AdminCreateCommand.RunAsync(Config(), "a@example.com", "correct horse battery", new StringWriter(), new StringWriter(), Ct);
        var error = new StringWriter();
        (await OperationsCommands.SetupTokenAsync(Config(), new StringWriter(), error, Ct)).ShouldBe(1);
        error.ToString().ShouldContain("already complete");
    }

    [Fact]
    [Trait("Req", "FR-OPS-02")]
    public async Task Health_prints_the_checks_and_exits_by_status()
    {
        var missing = new StringWriter();
        (await OperationsCommands.HealthAsync(Config(), missing, Ct)).ShouldBe(2);
        missing.ToString().ShouldContain("Status: Critical");

        await AdminCreateCommand.RunAsync(Config(), "a@example.com", "correct horse battery", new StringWriter(), new StringWriter(), Ct);
        var output = new StringWriter();
        var exit = await OperationsCommands.HealthAsync(Config(), output, Ct);
        output.ToString().ShouldContain("quick_check: ok");
        output.ToString().ShouldContain("[OK] Backups: No backup has been made yet", customMessage: "a fresh install has 48 hours (ADR 0037)");
        output.ToString().ShouldContain("Status: ");
        exit.ShouldBeInRange(0, 2); // the build machine's disk decides between OK, Warning and Critical
    }

    [Fact]
    [Trait("Req", "FR-ADM-01")]
    public async Task Internal_audit_records_an_entry()
    {
        await AdminCreateCommand.RunAsync(Config(), "a@example.com", "correct horse battery", new StringWriter(), new StringWriter(), Ct);
        (await OperationsCommands.InternalAuditAsync(Config(), "ApplyConfig.Run", "by root: /mnt/nas", Ct)).ShouldBe(0);

        await using var c = new SqliteConnection($"Data Source={new DataOptions { Directory = dir }.DatabasePath};Pooling=False");
        await c.OpenAsync(Ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT UserName FROM AuditEntries WHERE Action = 'ApplyConfig.Run';";
        ((string?)await cmd.ExecuteScalarAsync(Ct)).ShouldNotBeNull().ShouldStartWith("cli:");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }
}

/// <summary>AC-85: the root form of "recipewarden restore" — check, stop, apply, start, health — in that order.</summary>
[Trait("Req", "AC-85")]
[Trait("Req", "FR-BKP-07")]
public sealed class AC_85_RestoreOrchestratorTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("rw-cli-orch-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeHost : IHostRunner
    {
        public List<string> Calls { get; } = [];

        public int CheckExit { get; set; }

        public int ApplyExit { get; set; }

        public string? Owner { get; set; } = "recipewarden";

        public List<string?> Stdin { get; } = [];

        public Task<string?> OwnerOfAsync(string path, CancellationToken ct) => Task.FromResult(Owner);

        public Task<bool> ChownLikeAsync(string reference, IReadOnlyList<string> paths, CancellationToken ct)
        {
            Calls.Add("chown");
            return Task.FromResult(true);
        }

        public Task<int> RunCliAsAsync(string user, IReadOnlyList<string> args, string? stdin, CancellationToken ct)
        {
            Stdin.Add(stdin);
            var mode = args[^1];
            Calls.Add(user + " " + mode);
            File.Exists(args[3]).ShouldBeTrue("the staged archive exists while the child runs");
            return Task.FromResult(mode == "--check" ? CheckExit : ApplyExit);
        }
    }

    private sealed class Probe(bool healthy) : ApplyConfig.IServiceHealthProbe
    {
        public Task<bool> WaitHealthyAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(healthy);
    }

    private (RestoreOrchestrator Orchestrator, FakeHost Host, FakeSystemd Systemd, StringWriter Error) Build(bool healthy = true, string? confirmation = "RESTORE")
    {
        var host = new FakeHost();
        var systemd = new FakeSystemd();
        var error = new StringWriter();
        return (new RestoreOrchestrator(host, systemd, new Probe(healthy), new DataOptions { Directory = dir }, new StringWriter(), error, () => confirmation),
            host, systemd, error);
    }

    private string Archive()
    {
        var path = Path.Combine(dir, "backup.tar.gz");
        File.WriteAllBytes(path, [0x1F, 0x8B]);
        return path;
    }

    [Fact]
    public async Task Check_stop_apply_start_in_order_and_the_staging_copy_is_removed()
    {
        var (o, host, systemd, _) = Build();
        var kit = Path.Combine(dir, "kit.txt");
        await File.WriteAllTextAsync(kit, "kit", Ct);

        (await o.RunAsync(new RestoreRequest(Archive(), kit, "secret", Yes: false, "/etc/recipewarden/appsettings.Production.json"), Ct)).ShouldBe(0);

        host.Calls.ShouldBe(["chown", "recipewarden --check", "recipewarden --apply"]);
        systemd.Calls.ShouldBe(["stop recipewarden", "start recipewarden"]);
        host.Stdin.ShouldAllBe(s => s == "secret");
        Directory.EnumerateDirectories(Path.Combine(dir, "tmp"), "cli-restore-*").ShouldBeEmpty();
    }

    [Fact]
    [Trait("Req", "BR-65")]
    public async Task A_failed_check_such_as_a_newer_archive_stops_before_the_service_is_touched()
    {
        var (o, host, systemd, _) = Build();
        host.CheckExit = 1;

        (await o.RunAsync(new RestoreRequest(Archive(), null, null, true, "/c.json"), Ct)).ShouldBe(1);

        systemd.Calls.ShouldBeEmpty();
        host.Calls.ShouldNotContain("recipewarden --apply");
    }

    [Fact]
    public async Task Without_the_confirmation_word_nothing_changes()
    {
        var (o, host, systemd, error) = Build(confirmation: "yes");

        (await o.RunAsync(new RestoreRequest(Archive(), null, null, false, "/c.json"), Ct)).ShouldBe(1);

        error.ToString().ShouldContain("nothing was changed");
        systemd.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failed_apply_still_starts_the_service_and_reports_the_failure()
    {
        var (o, host, systemd, error) = Build();
        host.ApplyExit = 1;

        (await o.RunAsync(new RestoreRequest(Archive(), null, null, true, "/c.json"), Ct)).ShouldBe(1);

        systemd.Calls.ShouldBe(["stop recipewarden", "start recipewarden"]);
        error.ToString().ShouldContain("previous data was put back");
    }

    [Fact]
    public async Task Unhealthy_after_restore_is_an_error_and_missing_inputs_are_refused()
    {
        var (o, _, _, error) = Build(healthy: false);
        (await o.RunAsync(new RestoreRequest(Archive(), null, null, true, "/c.json"), Ct)).ShouldBe(1);
        error.ToString().ShouldContain("did not become healthy");

        (await o.RunAsync(new RestoreRequest(Path.Combine(dir, "missing.tar.gz"), null, null, true, "/c.json"), Ct)).ShouldBe(1);

        var (rootOwned, host, _, rootError) = Build();
        host.Owner = "root";
        (await rootOwned.RunAsync(new RestoreRequest(Archive(), null, null, true, "/c.json"), Ct)).ShouldBe(1);
        rootError.ToString().ShouldContain("service user");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }
}
