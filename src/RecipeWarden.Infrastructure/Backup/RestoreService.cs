using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RecipeWarden.Core.Backup;
using RecipeWarden.Infrastructure.Audit;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Backup;

/// <summary>What the person supplied to open an encrypted archive (FR-BKP-15). Never stored or logged (SEC-75).</summary>
public sealed record RecoveryKey(string KitText, string Passphrase);

/// <summary>An archive that passed every check and is unpacked in a quarantine folder, ready to apply.</summary>
public sealed class PreparedRestore(string workDirectory, BackupManifest manifest, int recipes, int users, int mediaFiles) : IDisposable
{
    public string WorkDirectory { get; } = workDirectory;
    public BackupManifest Manifest { get; } = manifest;
    public int Recipes { get; } = recipes;
    public int Users { get; } = users;
    public int MediaFiles { get; } = mediaFiles;

    public string Staging => Path.Combine(WorkDirectory, "staging");

    /// <summary>The quarantine folder's random id, used to confirm this preparation in a second request.</summary>
    public string Id => Path.GetFileName(WorkDirectory)["restore-".Length..];

    public void Dispose() => BackupService.TryDeleteDirectory(WorkDirectory);
}

public sealed record RestorePreparation(PreparedRestore? Prepared, string? Error);

public sealed record RestoreOptions(bool PreRestoreBackup = true, string? Origin = null);

/// <summary>
/// FR-BKP-04, 06, 07, BR-62 to BR-66, SEC-54, SEC-75, ADR 0040. <see cref="PrepareAsync"/> changes nothing: it
/// decrypts with the recovery kit (wrong kit or passphrase fails here), checks every tar entry before writing it
/// (no absolute paths, no "..", no links, size caps) into a quarantine folder, checks the manifest, every SHA-256,
/// the version (BR-65) and the database's integrity. <see cref="ApplyAsync"/> then takes a pre-restore backup,
/// copies the database in with SQLite's online backup API, swaps the media folder, adds the archive's Data
/// Protection keys, migrates, and rolls everything back if any step fails. Config and network settings are never
/// taken from an archive (BR-64).
/// </summary>
public sealed partial class RestoreService(
    AppDbContext db,
    DataOptions data,
    BackupFolders folders,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<RestoreService> logger)
{
    public const string ChecksumMismatch = "The archive was changed or damaged: a checksum does not match its manifest.";

    public async Task<RestorePreparation> PrepareAsync(string archivePath, RecoveryKey? key, ArchiveLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        limits ??= ArchiveLimits.Default;
        RemoveStale();
        var work = Directory.CreateDirectory(Path.Combine(data.WorkDirectory, "restore-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var error = await UnpackAsync(archivePath, key, Path.Combine(work, "staging"), limits, cancellationToken).ConfigureAwait(false);
            if (error is not null)
            {
                BackupService.TryDeleteDirectory(work);
                return new RestorePreparation(null, error);
            }

            var staging = Path.Combine(work, "staging");
            var manifestPath = Path.Combine(staging, BackupManifest.FileName);
            BackupManifest? manifest;
            try
            {
                manifest = File.Exists(manifestPath)
                    ? JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false), ManifestJson.Options)
                    : null;
            }
            catch (JsonException)
            {
                manifest = null;
            }

            error = ArchiveRules.CheckManifest(manifest)
                ?? ArchiveRules.CheckVersion(manifest!, RuntimeInfo.AppVersion, db.Database.GetMigrations().ToList())
                ?? await VerifyFilesAsync(staging, manifest!, cancellationToken).ConfigureAwait(false);
            if (error is not null)
            {
                BackupService.TryDeleteDirectory(work);
                return new RestorePreparation(null, error);
            }

            var dbPath = Path.Combine(staging, "db", "recipewarden.db");
            var (integrity, recipes, users) = Inspect(dbPath);
            if (!string.Equals(integrity, "ok", StringComparison.Ordinal))
            {
                BackupService.TryDeleteDirectory(work);
                return new RestorePreparation(null, "The database in the archive failed its integrity check: " + integrity);
            }

            var media = manifest!.Files.Count(f => f.Path.StartsWith("media/", StringComparison.Ordinal));
            return new RestorePreparation(new PreparedRestore(work, manifest, recipes, users, media), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or SqliteException or FormatException)
        {
            LogPrepareFailed(logger, ex);
            BackupService.TryDeleteDirectory(work);
            return new RestorePreparation(null, "The archive could not be read: " + ex.Message);
        }
    }

    public static readonly TimeSpan PreparedLifetime = TimeSpan.FromHours(2);

    /// <summary>
    /// Opens a preparation made in an earlier request (the confirmation step, W7). The id must be a quarantine folder
    /// this service created; the files are checked again, since time has passed.
    /// </summary>
    public async Task<RestorePreparation> ReopenAsync(string? id, CancellationToken cancellationToken = default)
    {
        if (id is null || id.Length != 32 || !id.All(char.IsAsciiHexDigitLower))
        {
            return new RestorePreparation(null, "That restore is no longer waiting. Start again.");
        }

        var work = Path.Combine(data.WorkDirectory, "restore-" + id);
        var manifestPath = Path.Combine(work, "staging", BackupManifest.FileName);
        if (!File.Exists(manifestPath) || clock.GetUtcNow() - File.GetLastWriteTimeUtc(manifestPath) > PreparedLifetime)
        {
            return new RestorePreparation(null, "That restore is no longer waiting. Start again.");
        }

        var manifest = JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false), ManifestJson.Options)!;
        var staging = Path.Combine(work, "staging");
        if (await VerifyFilesAsync(staging, manifest, cancellationToken).ConfigureAwait(false) is { } error)
        {
            return new RestorePreparation(null, error);
        }

        var (integrity, recipes, users) = Inspect(Path.Combine(staging, "db", "recipewarden.db"));
        return integrity == "ok"
            ? new RestorePreparation(new PreparedRestore(work, manifest, recipes, users, manifest.Files.Count(f => f.Path.StartsWith("media/", StringComparison.Ordinal))), null)
            : new RestorePreparation(null, "The database in the archive failed its integrity check: " + integrity);
    }

    /// <summary>Quarantine folders left by abandoned restores are removed after two hours.</summary>
    private void RemoveStale()
    {
        if (!Directory.Exists(data.WorkDirectory))
        {
            return;
        }

        foreach (var dir in Directory.EnumerateDirectories(data.WorkDirectory, "restore-*"))
        {
            if (clock.GetUtcNow() - Directory.GetLastWriteTimeUtc(dir) > PreparedLifetime)
            {
                BackupService.TryDeleteDirectory(dir);
            }
        }
    }

    /// <summary>Applies a prepared archive. The caller holds <see cref="MaintenanceMode"/> (web) or has stopped the service (CLI).</summary>
    public async Task<string?> ApplyAsync(PreparedRestore prepared, RestoreOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(options);

        if (options.PreRestoreBackup)
        {
            await using var backupScope = scopes.CreateAsyncScope();
            var outcome = await backupScope.ServiceProvider.GetRequiredService<BackupService>()
                .RunAsync(new BackupRequest(BackupKind.PreRestore, folders.Local), cancellationToken).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                return "The pre-restore backup failed, so nothing was restored: " + outcome.Error;
            }
        }

        // The backup history describes files on disk, which a restore does not touch: keep the rows the archive lacks
        // (the pre-restore backup above among them), so every archive stays listed (ADR 0040).
        var history = await db.BackupRecords.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

        var stamp = clock.GetUtcNow().ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var rollbackDb = Path.Combine(prepared.WorkDirectory, "rollback.db");
        var oldMedia = data.MediaDirectory + ".pre-restore-" + stamp;
        var stagedMedia = Path.Combine(prepared.Staging, "media");
        var movedMedia = false;
        var copiedDb = false;
        var addedKeys = new List<string>();
        try
        {
            CopyDatabase(data.DatabasePath, rollbackDb);
            CopyDatabase(Path.Combine(prepared.Staging, "db", "recipewarden.db"), data.DatabasePath);
            copiedDb = true;

            if (Directory.Exists(data.MediaDirectory))
            {
                Directory.Move(data.MediaDirectory, oldMedia);
            }

            movedMedia = true;
            if (Directory.Exists(stagedMedia))
            {
                Directory.Move(stagedMedia, data.MediaDirectory);
            }
            else
            {
                Directory.CreateDirectory(data.MediaDirectory);
            }

            // ADR 0038: the archive's Data Protection keys join the key ring, so sessions and stored secrets still decrypt.
            var stagedKeys = Path.Combine(prepared.Staging, "keys");
            if (Directory.Exists(stagedKeys))
            {
                Directory.CreateDirectory(data.KeysDirectory);
                foreach (var file in Directory.EnumerateFiles(stagedKeys, "*.xml"))
                {
                    var target = Path.Combine(data.KeysDirectory, Path.GetFileName(file));
                    if (!File.Exists(target))
                    {
                        File.Copy(file, target);
                        addedKeys.Add(target);
                    }
                }
            }

            SqliteConnection.ClearAllPools();
            await using (var scope = scopes.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(cancellationToken).ConfigureAwait(false);
                await MergeHistoryAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), history, cancellationToken).ConfigureAwait(false);
                await scope.ServiceProvider.GetRequiredService<AuditLog>().RecordAsync("Restore.Run", "Backup", prepared.Manifest.CreatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                    new { from = options.Origin, appVersion = prepared.Manifest.AppVersion, schema = prepared.Manifest.SchemaVersion, recipes = prepared.Recipes, users = prepared.Users },
                    cancellationToken).ConfigureAwait(false);
            }

            BackupService.TryDeleteDirectory(oldMedia);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or InvalidOperationException or DbUpdateException)
        {
            LogApplyFailed(logger, ex);
            Rollback(copiedDb, rollbackDb, movedMedia, oldMedia, addedKeys);
            return "The restore failed and the previous data was put back: " + ex.Message;
        }
    }

    private static async Task MergeHistoryAsync(AppDbContext restored, List<BackupRecord> history, CancellationToken ct)
    {
        using var quiet = AuditScope.Suppress();
        var known = (await restored.BackupRecords.AsNoTracking().Select(r => new { r.DestinationFolder, r.FileName }).ToListAsync(ct).ConfigureAwait(false))
            .Select(r => r.DestinationFolder + "/" + r.FileName).ToHashSet(StringComparer.Ordinal);
        var schedules = (await restored.BackupSchedules.AsNoTracking().Select(s => s.Id).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        foreach (var r in history.Where(r => r.FileName is not null && r.Status == BackupStatus.Succeeded && !known.Contains(r.DestinationFolder + "/" + r.FileName)))
        {
            r.Id = 0;
            r.ScheduleId = r.ScheduleId is { } sid && schedules.Contains(sid) ? sid : null;
            restored.BackupRecords.Add(r);
        }

        await restored.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private void Rollback(bool copiedDb, string rollbackDb, bool movedMedia, string oldMedia, List<string> addedKeys)
    {
        try
        {
            if (copiedDb && File.Exists(rollbackDb))
            {
                CopyDatabase(rollbackDb, data.DatabasePath);
            }

            if (movedMedia && Directory.Exists(oldMedia))
            {
                BackupService.TryDeleteDirectory(data.MediaDirectory);
                Directory.Move(oldMedia, data.MediaDirectory);
            }

            foreach (var key in addedKeys)
            {
                BackupService.TryDelete(key);
            }

            SqliteConnection.ClearAllPools();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            LogRollbackFailed(logger, ex);
        }
    }

    /// <summary>How long a copy waits for another connection's lock on either file before it fails (30 s).</summary>
    private const string CopyBusyTimeout = "PRAGMA busy_timeout = 30000";

    /// <summary>
    /// SQLite online backup from one file into another (the live database stays a valid WAL database). Both connections
    /// get SQLite's own busy timeout: Microsoft.Data.Sqlite retries commands on a lock but not a backup, which otherwise
    /// fails at once with "database is locked" if any other connection holds a lock for a moment (FR-BKP-01).
    /// </summary>
    internal static void CopyDatabase(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = from, Pooling = false }.ToString());
        using var dest = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = to, Pooling = false }.ToString());
        source.Open();
        dest.Open();
        BusyTimeout(source);
        BusyTimeout(dest);
        source.BackupDatabase(dest);
    }

    private static void BusyTimeout(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = CopyBusyTimeout;
        command.ExecuteNonQuery();
    }

    private static (string? Integrity, int Recipes, int Users) Inspect(string dbPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        var integrity = BackupService.Pragma(connection, SqlitePragma.IntegrityCheck);
        return (integrity, Count(connection, SqlitePragma.CountRecipes), Count(connection, SqlitePragma.CountUsers));
    }

    private static int Count(SqliteConnection connection, SqlitePragma count)
    {
        try
        {
            return int.Parse(BackupService.Pragma(connection, count) ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (SqliteException)
        {
            return 0;
        }
    }

    /// <summary>SEC-54: every entry is checked before it is written, and only into <paramref name="staging"/>.</summary>
    private static async Task<string?> UnpackAsync(string archivePath, RecoveryKey? key, string staging, ArchiveLimits limits, CancellationToken ct)
    {
        Directory.CreateDirectory(staging);
        var file = File.OpenRead(archivePath);
        await using (file.ConfigureAwait(false))
        {
            var header = new byte[BackupCrypto.Magic.Length];
            var read = await file.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct).ConfigureAwait(false);
            file.Position = 0;
            Stream source = file;
            System.Security.Cryptography.RSA? rsa = null;
            try
            {
                if (read == header.Length && BackupCrypto.IsEncrypted(header))
                {
                    if (key is null)
                    {
                        return "This archive is encrypted. Give the recovery kit and its passphrase.";
                    }

                    rsa = BackupCrypto.OpenKit(key.KitText, key.Passphrase);
                    source = BackupCrypto.Decrypt(file, rsa, leaveOpen: true);
                }
                else if (read < 2 || header[0] != 0x1F || header[1] != 0x8B)
                {
                    return "That file is not a RecipeWarden backup (.tar.gz or encrypted .rwenc).";
                }

                return await ExtractAsync(source, staging, limits, ct).ConfigureAwait(false);
            }
            catch (BackupKeyException ex)
            {
                return ex.Message;
            }
            finally
            {
                if (!ReferenceEquals(source, file))
                {
                    await source.DisposeAsync().ConfigureAwait(false);
                }

                rsa?.Dispose();
            }
        }
    }

    private static async Task<string?> ExtractAsync(Stream source, string staging, ArchiveLimits limits, CancellationToken ct)
    {
        var gzip = new GZipStream(source, CompressionMode.Decompress, leaveOpen: true);
        await using (gzip.ConfigureAwait(false))
        {
            var reader = new TarReader(gzip, leaveOpen: true);
            await using (reader.ConfigureAwait(false))
            {
                long total = 0;
                var count = 0;
                var root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
                while (await reader.GetNextEntryAsync(copyData: false, ct).ConfigureAwait(false) is { } entry)
                {
                    if (++count > limits.MaxEntries)
                    {
                        return "The archive has too many files.";
                    }

                    if (ArchiveRules.CheckEntryName(entry.Name) is { } problem)
                    {
                        return "The archive was rejected: " + problem + ".";
                    }

                    if (entry.EntryType is TarEntryType.Directory)
                    {
                        continue;
                    }

                    if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                    {
                        return $"The archive was rejected: \"{entry.Name}\" is a link or a special file.";
                    }

                    total += entry.Length;
                    if (entry.Length > limits.MaxFileBytes || total > limits.MaxTotalBytes)
                    {
                        return "The archive is larger than the restore limit.";
                    }

                    var target = Path.GetFullPath(Path.Combine(staging, entry.Name));
                    if (!target.StartsWith(root, StringComparison.Ordinal))
                    {
                        return "The archive was rejected: an entry points outside the restore folder.";
                    }

                    if (File.Exists(target))
                    {
                        return $"The archive was rejected: \"{entry.Name}\" appears twice.";
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
                    await using (output.ConfigureAwait(false))
                    {
                        if (entry.DataStream is not null)
                        {
                            await CopyCappedAsync(entry.DataStream, output, entry.Length, ct).ConfigureAwait(false);
                        }
                    }
                }

                return count == 0 ? "The archive is empty." : null;
            }
        }
    }

    private static async Task CopyCappedAsync(Stream from, Stream to, long expected, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long copied = 0;
        int n;
        while ((n = await from.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            copied += n;
            if (copied > expected)
            {
                throw new InvalidDataException("An entry is longer than its header says.");
            }

            await to.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
        }
    }

    /// <summary>BR-62: every listed file exists with its size and SHA-256, and nothing unlisted came with it.</summary>
    private static async Task<string?> VerifyFilesAsync(string staging, BackupManifest manifest, CancellationToken ct)
    {
        foreach (var f in manifest.Files)
        {
            var path = Path.Combine(staging, f.Path);
            if (!File.Exists(path) || new FileInfo(path).Length != f.Bytes
                || !string.Equals(await BackupService.Sha256Async(path, ct).ConfigureAwait(false), f.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return ChecksumMismatch;
            }
        }

        var listed = manifest.Files.Select(f => f.Path).Append(BackupManifest.FileName).ToHashSet(StringComparer.Ordinal);
        var extra = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(staging, p).Replace('\\', '/'))
            .FirstOrDefault(p => !listed.Contains(p));
        return extra is null ? null : $"The archive was rejected: \"{extra}\" is not in its manifest.";
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Restore preparation failed")]
    private static partial void LogPrepareFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Restore failed; rolling back")]
    private static partial void LogApplyFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Restore rollback failed; restore from the pre-restore backup")]
    private static partial void LogRollbackFailed(ILogger logger, Exception exception);
}
