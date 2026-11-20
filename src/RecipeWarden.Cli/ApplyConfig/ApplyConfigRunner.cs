using RecipeWarden.Core.Backup;

namespace RecipeWarden.Cli.ApplyConfig;

internal sealed record ApplyConfigRequest(IReadOnlyList<string?> ConfiguredFolders, bool DryRun, bool NoRestart, bool IsPrivileged, string OsUser)
{
    /// <summary>Write the drop-in only: no daemon-reload, no restart (installer inside a container, --no-start).</summary>
    public bool NoReload { get; init; }
}

/// <summary>
/// FR-BKP-09 (BKP-SVC-001), AC-86: validate Backup:AllowedFolders, write or remove the systemd drop-in,
/// restart, wait for health, and put the previous drop-in back if health fails.
/// </summary>
internal sealed class ApplyConfigRunner(IHostFileSystem fs, ISystemd systemd, IServiceHealthProbe health, TextWriter output, TextWriter error)
{
    public const string ServiceUnit = "recipewarden.service";
    public static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(60);

    public const int ExitOk = 0;
    public const int ExitInvalidConfig = 2;
    public const int ExitNotPrivileged = 3;
    public const int ExitSystemdFailed = 4;
    public const int ExitHealthFailedRolledBack = 5;

    public async Task<int> RunAsync(ApplyConfigRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Validate without touching anything.
        var validation = BackupFolderPolicy.Validate(request.ConfiguredFolders, fs.Probe);
        foreach (var w in validation.Warnings)
        {
            await output.WriteLineAsync($"warning: {w.Folder}: {w.Reason}");
        }

        if (!validation.IsValid)
        {
            foreach (var e in validation.Errors)
            {
                await error.WriteLineAsync($"error: {(e.Folder.Length == 0 ? "(empty)" : e.Folder)}: {e.Reason}");
            }

            await error.WriteLineAsync("Backup:AllowedFolders is invalid. Nothing was changed.");
            return ExitInvalidConfig;
        }

        var previous = fs.ReadAllTextOrNull(BackupFoldersDropIn.FilePath);
        var next = BackupFoldersDropIn.Render(validation.Folders);
        var before = BackupFoldersDropIn.Parse(previous);
        var after = validation.Folders;

        await PrintChangesAsync(before, after);

        if (string.Equals(previous, next, StringComparison.Ordinal))
        {
            await output.WriteLineAsync("The drop-in is already up to date. Nothing to apply.");
            return ExitOk;
        }

        if (request.DryRun)
        {
            await output.WriteLineAsync(next is null
                ? $"--dry-run: would remove {BackupFoldersDropIn.FilePath}"
                : $"--dry-run: would write {BackupFoldersDropIn.FilePath}:\n{next}");
            return ExitOk;
        }

        if (!request.IsPrivileged)
        {
            await error.WriteLineAsync("apply-config must run as root: sudo recipewarden apply-config");
            return ExitNotPrivileged;
        }

        // 2. Write (or remove) our drop-in only; override.conf is never touched.
        WriteDropIn(next);
        await output.WriteLineAsync($"audit: apply-config by {request.OsUser}: before=[{string.Join(", ", before)}] after=[{string.Join(", ", after)}]");

        if (request.NoReload)
        {
            await output.WriteLineAsync("--no-reload: systemd was not reloaded.");
            return ExitOk;
        }

        if (!await systemd.DaemonReloadAsync(cancellationToken))
        {
            WriteDropIn(previous);
            await error.WriteLineAsync("systemctl daemon-reload failed; the previous drop-in was restored.");
            return ExitSystemdFailed;
        }

        if (request.NoRestart)
        {
            await output.WriteLineAsync($"--no-restart: restart {ServiceUnit} yourself for the change to take effect.");
            return ExitOk;
        }

        // 3. Restart and wait for health; roll back on failure.
        var restarted = await systemd.RestartAsync(ServiceUnit, cancellationToken);
        if (restarted && await health.WaitHealthyAsync(HealthTimeout, cancellationToken))
        {
            await output.WriteLineAsync($"{ServiceUnit} restarted and healthy.");
            return ExitOk;
        }

        await error.WriteLineAsync($"{ServiceUnit} did not become healthy. Restoring the previous drop-in.");
        WriteDropIn(previous);
        await systemd.DaemonReloadAsync(cancellationToken);
        await systemd.RestartAsync(ServiceUnit, cancellationToken);
        await output.WriteLineAsync($"audit: apply-config rolled back by {request.OsUser}: restored=[{string.Join(", ", before)}]");
        return ExitHealthFailedRolledBack;
    }

    private void WriteDropIn(string? content)
    {
        if (content is null)
        {
            fs.Delete(BackupFoldersDropIn.FilePath);
        }
        else
        {
            fs.WriteAllTextAtomic(BackupFoldersDropIn.FilePath, content);
        }
    }

    private async Task PrintChangesAsync(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        foreach (var added in after.Except(before, StringComparer.Ordinal))
        {
            await output.WriteLineAsync($"+ {added}");
        }

        foreach (var removed in before.Except(after, StringComparer.Ordinal))
        {
            await output.WriteLineAsync($"- {removed}");
        }
    }
}
