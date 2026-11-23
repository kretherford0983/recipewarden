using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RecipeWarden.Cli.ApplyConfig;
using RecipeWarden.Core.Backup;
using RecipeWarden.Core.Health;
using RecipeWarden.Infrastructure;
using RecipeWarden.Infrastructure.Audit;
using RecipeWarden.Infrastructure.Backup;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Health;
using RecipeWarden.Infrastructure.Setup;

namespace RecipeWarden.Cli.Operations;

/// <summary>The app's services for a CLI command, with the CLI as the audit actor ("cli:&lt;os user&gt;").</summary>
internal sealed class CliScope : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private readonly AsyncServiceScope scope;
    private CliScope(ServiceProvider provider)
    {
        this.provider = provider;
        scope = provider.CreateAsyncScope();
    }

    public static string OsUser => Environment.GetEnvironmentVariable("SUDO_USER") ?? Environment.UserName;

    public IServiceProvider Services => scope.ServiceProvider;

    public static async Task<CliScope> OpenAsync(IConfiguration config, CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IAuditActor>(new CliActor());
        services.AddRecipeWardenData(config);
        services.AddRecipeWardenIdentityCore();
        var cli = new CliScope(services.BuildServiceProvider());
        await cli.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync(cancellationToken).ConfigureAwait(false);
        return cli;
    }

    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync().ConfigureAwait(false);
        await provider.DisposeAsync().ConfigureAwait(false);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }
}

/// <summary>FR-ADM-01: writes made by a CLI command are audited as "cli:&lt;os user&gt;".</summary>
internal sealed class CliActor : IAuditActor
{
    public AuditActorInfo Current => AuditScope.Actor ?? new AuditActorInfo(null, "cli:" + CliScope.OsUser, null);
}

/// <summary>
/// Section 9.5, FR-BKP-01, FR-BKP-07, FR-INST-03, SEC-15: "recipewarden backup", "restore", "setup-token" and "health".
/// They run as the service user (the host wrapper switches to it), so every file they create belongs to it; only
/// "restore" starts as root, to stop and start the service around the part that runs as the service user.
/// </summary>
internal static class OperationsCommands
{
    public const string Unit = "recipewarden";

    /// <summary>"recipewarden backup [--folder F] [--kind manual|pre-upgrade]": one archive now; prints its path.</summary>
    public static async Task<int> BackupAsync(IConfiguration config, string? folder, string? kind, TextWriter output, TextWriter error, CancellationToken ct)
    {
        var backupKind = (kind ?? "manual").ToLowerInvariant() switch
        {
            "manual" => BackupKind.Manual,
            "pre-upgrade" => BackupKind.PreUpgrade,
            _ => (BackupKind?)null,
        };
        if (backupKind is null)
        {
            await error.WriteLineAsync("error: --kind must be manual or pre-upgrade.").ConfigureAwait(false);
            return 1;
        }

        await using var cli = await CliScope.OpenAsync(config, ct).ConfigureAwait(false);
        var folders = cli.Services.GetRequiredService<BackupFolders>();
        var target = folders.Resolve(folder ?? folders.Local);
        if (target is null)
        {
            await error.WriteLineAsync($"error: {folder} is not in Backup:AllowedFolders. Allowed: {string.Join(", ", folders.Allowed())}").ConfigureAwait(false);
            return 1;
        }

        var outcome = await cli.Services.GetRequiredService<BackupService>().RunAsync(new BackupRequest(backupKind.Value, target), ct).ConfigureAwait(false);
        if (!outcome.Ok)
        {
            await error.WriteLineAsync("error: backup failed: " + outcome.Error).ConfigureAwait(false);
            return 1;
        }

        await output.WriteLineAsync(outcome.Record.FullPath).ConfigureAwait(false);
        await output.WriteLineAsync($"  {HealthRules.FormatBytes(outcome.Record.Bytes)}, SHA-256 {outcome.Record.Sha256}{(outcome.Record.Encrypted ? ", encrypted" : ", not encrypted")}")
            .ConfigureAwait(false);
        return 0;
    }

    /// <summary>"recipewarden setup-token": a one-time token for /setup, while no Admin exists (SEC-15).</summary>
    public static async Task<int> SetupTokenAsync(IConfiguration config, TextWriter output, TextWriter error, CancellationToken ct)
    {
        await using var cli = await CliScope.OpenAsync(config, ct).ConfigureAwait(false);
        var token = await cli.Services.GetRequiredService<SetupService>().IssueTokenAsync().ConfigureAwait(false);
        if (token is null)
        {
            await error.WriteLineAsync("Setup is already complete (an Admin account exists). Sign in, or use: recipewarden admin create --email ...").ConfigureAwait(false);
            return 1;
        }

        await output.WriteLineAsync(token).ConfigureAwait(false);
        return 0;
    }

    /// <summary>"recipewarden health": the same checks as /admin/health, for SSH use. Exit 0 OK, 1 Warning, 2 Critical.</summary>
    public static async Task<int> HealthAsync(IConfiguration config, TextWriter output, CancellationToken ct)
    {
        // The database basics first (read-only: present, quick_check, WAL); a broken database stops here.
        var basics = new StringWriter();
        var basic = await Health.HealthCommand.RunAsync(config, basics, ct).ConfigureAwait(false);
        foreach (var line in basics.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => !l.StartsWith("Status:", StringComparison.Ordinal)))
        {
            await output.WriteLineAsync(line).ConfigureAwait(false);
        }

        if (basic != 0)
        {
            await output.WriteLineAsync("Status: Critical").ConfigureAwait(false);
            return 2;
        }

        await using var cli = await CliScope.OpenAsync(config, ct).ConfigureAwait(false);
        var report = await cli.Services.GetRequiredService<HealthService>().EvaluateAsync(store: false, ct).ConfigureAwait(false);
        await output.WriteLineAsync($"RecipeWarden {RuntimeInfo.AppVersion}; schema {report.Database.SchemaVersion}").ConfigureAwait(false);
        foreach (var v in report.Volumes)
        {
            await output.WriteLineAsync($"Volume {v.Label}: {HealthRules.FormatBytes(v.FreeBytes)} free of {HealthRules.FormatBytes(v.TotalBytes)}").ConfigureAwait(false);
        }

        foreach (var c in report.Checks)
        {
            await output.WriteLineAsync($"  [{c.Status}] {c.Name}: {c.Message}").ConfigureAwait(false);
        }

        if (report.Recommendation is not null)
        {
            await output.WriteLineAsync("Recommendation: " + report.Recommendation).ConfigureAwait(false);
        }

        await output.WriteLineAsync($"Status: {report.Status}").ConfigureAwait(false);
        return report.Status switch
        {
            HealthStatus.OK => 0,
            HealthStatus.Warning => 1,
            _ => 2,
        };
    }

    /// <summary>
    /// "recipewarden restore &lt;archive&gt; --check|--apply" as the service user: --check validates and reports, changing
    /// nothing; --apply validates again and restores (with a pre-restore backup unless --no-pre-restore). The service must
    /// be stopped for --apply (the root form of the command does that).
    /// </summary>
    public static async Task<int> RestoreOfflineAsync(IConfiguration config, string archive, string? kitPath, string? passphrase, bool apply, bool preRestore,
        string origin, TextWriter output, TextWriter error, CancellationToken ct)
    {
        if (!File.Exists(archive))
        {
            await error.WriteLineAsync($"error: {archive} does not exist.").ConfigureAwait(false);
            return 1;
        }

        RecoveryKey? key = null;
        if (kitPath is not null)
        {
            if (!File.Exists(kitPath) || new FileInfo(kitPath).Length > 64 * 1024)
            {
                await error.WriteLineAsync($"error: {kitPath} is not a recovery kit.").ConfigureAwait(false);
                return 1;
            }

            key = new RecoveryKey(await File.ReadAllTextAsync(kitPath, ct).ConfigureAwait(false), passphrase ?? string.Empty);
        }

        await using var cli = await CliScope.OpenAsync(config, ct).ConfigureAwait(false);
        var restore = cli.Services.GetRequiredService<RestoreService>();
        var prepared = await restore.PrepareAsync(archive, key, cancellationToken: ct).ConfigureAwait(false);
        if (prepared.Error is not null)
        {
            await error.WriteLineAsync("Restore refused (nothing was changed): " + prepared.Error).ConfigureAwait(false);
            return 1;
        }

        using var p = prepared.Prepared!;
        await output.WriteLineAsync($"Archive OK: made {p.Manifest.CreatedAt:yyyy-MM-dd HH:mm} UTC by RecipeWarden {p.Manifest.AppVersion}; {p.Recipes} recipes, {p.Users} accounts, {p.MediaFiles} image files.")
            .ConfigureAwait(false);
        if (!apply)
        {
            return 0;
        }

        var applied = await restore.ApplyAsync(p, new RestoreOptions(preRestore, origin), ct).ConfigureAwait(false);
        if (applied is not null)
        {
            await error.WriteLineAsync("error: " + applied).ConfigureAwait(false);
            return 1;
        }

        cli.Services.GetRequiredService<SetupService>().Complete("restore"); // BR-66: an install-time restore disables /setup
        await output.WriteLineAsync("Restored and migrated.").ConfigureAwait(false);
        return 0;
    }

    /// <summary>Hidden: records an audit entry for a root-run command (apply-config), run as the service user.</summary>
    public static async Task<int> InternalAuditAsync(IConfiguration config, string action, string? details, CancellationToken ct)
    {
        await using var cli = await CliScope.OpenAsync(config, ct).ConfigureAwait(false);
        await cli.Services.GetRequiredService<AuditLog>().RecordAsync(action, "Config", null, details is null ? null : new { details }, ct).ConfigureAwait(false);
        return 0;
    }
}

/// <summary>Runs this CLI again as another user, and other small host programs, without a shell (SEC-24).</summary>
internal interface IHostRunner
{
    /// <summary>The user that owns <paramref name="path"/> (the service user owns the data directory).</summary>
    Task<string?> OwnerOfAsync(string path, CancellationToken ct);

    /// <summary>Gives <paramref name="paths"/> to the owner of <paramref name="reference"/>.</summary>
    Task<bool> ChownLikeAsync(string reference, IReadOnlyList<string> paths, CancellationToken ct);

    /// <summary>Runs this CLI as <paramref name="user"/> with <paramref name="args"/>, sending <paramref name="stdin"/>, echoing its output.</summary>
    Task<int> RunCliAsAsync(string user, IReadOnlyList<string> args, string? stdin, CancellationToken ct);
}

internal sealed class HostRunner(TextWriter output, TextWriter error) : IHostRunner
{
    public async Task<string?> OwnerOfAsync(string path, CancellationToken ct)
    {
        var (code, text) = await RunAsync("stat", ["-c", "%U", path], null, capture: true, ct).ConfigureAwait(false);
        return code == 0 && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
    }

    public async Task<bool> ChownLikeAsync(string reference, IReadOnlyList<string> paths, CancellationToken ct) =>
        (await RunAsync("chown", ["-R", "--reference=" + reference, "--", .. paths], null, capture: false, ct).ConfigureAwait(false)).Code == 0;

    public async Task<int> RunCliAsAsync(string user, IReadOnlyList<string> args, string? stdin, CancellationToken ct)
    {
        var self = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot find the CLI's own path.");
        return (await RunAsync("runuser", ["-u", user, "--", self, .. args], stdin, capture: false, ct).ConfigureAwait(false)).Code;
    }

    private async Task<(int Code, string Text)> RunAsync(string file, IReadOnlyList<string> args, string? stdin, bool capture, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start " + file);
        if (stdin is not null)
        {
            await process.StandardInput.WriteLineAsync(stdin).ConfigureAwait(false);
            process.StandardInput.Close();
        }

        var outTask = process.StandardOutput.ReadToEndAsync(ct);
        var errTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var stdout = await outTask.ConfigureAwait(false);
        var stderr = await errTask.ConfigureAwait(false);
        if (!capture)
        {
            await output.WriteAsync(stdout).ConfigureAwait(false);
        }

        await error.WriteAsync(stderr).ConfigureAwait(false);
        return (process.ExitCode, stdout);
    }
}

public sealed record RestoreRequest(string Archive, string? KitPath, string? Passphrase, bool Yes, string ConfigPath);

/// <summary>
/// FR-BKP-07, AC-85: "sudo recipewarden restore &lt;archive&gt;" works even when the web UI is down. As root it copies the
/// archive (and kit) into the data directory for the service user, checks it as that user (nothing changes if it fails,
/// including an archive from a newer version, BR-65), asks for the confirmation word, stops the service, restores as
/// the service user (pre-restore backup, restore, migrations), starts the service and waits for /health.
/// </summary>
internal sealed class RestoreOrchestrator(IHostRunner host, ISystemd systemd, IServiceHealthProbe health, DataOptions data, TextWriter output, TextWriter error,
    Func<string?> readConfirmation)
{
    public async Task<int> RunAsync(RestoreRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!File.Exists(request.Archive))
        {
            await error.WriteLineAsync($"error: {request.Archive} does not exist.").ConfigureAwait(false);
            return 1;
        }

        var user = await host.OwnerOfAsync(data.Directory, ct).ConfigureAwait(false);
        if (user is null or "root")
        {
            await error.WriteLineAsync($"error: cannot find the service user that owns {data.Directory}.").ConfigureAwait(false);
            return 1;
        }

        var staging = Path.Combine(data.WorkDirectory, "cli-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var archive = Path.Combine(staging, Path.GetFileName(request.Archive));
            File.Copy(request.Archive, archive);
            var args = new List<string> { "--config", request.ConfigPath, "restore", archive };
            if (request.KitPath is not null)
            {
                var kit = Path.Combine(staging, "kit.txt");
                File.Copy(request.KitPath, kit);
                args.AddRange(["--kit", kit, "--passphrase-stdin"]);
            }

            // The work folder (and anything root just made in it) must belong to the service user.
            if (!await host.ChownLikeAsync(data.Directory, [data.WorkDirectory], ct).ConfigureAwait(false))
            {
                await error.WriteLineAsync("error: could not hand the archive to the service user.").ConfigureAwait(false);
                return 1;
            }

            await output.WriteLineAsync("==> checking the archive").ConfigureAwait(false);
            if (await host.RunCliAsAsync(user, [.. args, "--check"], request.Passphrase, ct).ConfigureAwait(false) != 0)
            {
                return 1; // nothing changed
            }

            if (!request.Yes)
            {
                await output.WriteLineAsync($"Everything in {data.Directory} will be replaced by the archive (a pre-restore backup is made first).").ConfigureAwait(false);
                await output.WriteAsync($"Type {RestoreRules.ConfirmationWord} to continue: ").ConfigureAwait(false);
                if (!RestoreRules.IsConfirmed(readConfirmation()))
                {
                    await error.WriteLineAsync("Cancelled; nothing was changed.").ConfigureAwait(false);
                    return 1;
                }
            }

            await output.WriteLineAsync("==> stopping the service").ConfigureAwait(false);
            await systemd.StopAsync(OperationsCommands.Unit, ct).ConfigureAwait(false);
            await output.WriteLineAsync("==> restoring (pre-restore backup, restore, migrations)").ConfigureAwait(false);
            var applied = await host.RunCliAsAsync(user, [.. args, "--apply"], request.Passphrase, ct).ConfigureAwait(false);

            await output.WriteLineAsync("==> starting the service").ConfigureAwait(false);
            var started = await systemd.StartAsync(OperationsCommands.Unit, ct).ConfigureAwait(false)
                && await health.WaitHealthyAsync(TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);
            if (applied != 0)
            {
                await error.WriteLineAsync("error: the restore failed; the previous data was put back" + (started ? " and the service is running." : ".")).ConfigureAwait(false);
                return 1;
            }

            if (!started)
            {
                await error.WriteLineAsync("error: the service did not become healthy after the restore; see journalctl -u recipewarden.").ConfigureAwait(false);
                return 1;
            }

            await output.WriteLineAsync("Restore complete; the service is healthy.").ConfigureAwait(false);
            return 0;
        }
        finally
        {
            BackupService.TryDeleteDirectory(staging);
        }
    }
}
