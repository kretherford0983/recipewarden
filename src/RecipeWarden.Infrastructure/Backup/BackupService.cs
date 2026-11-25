using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RecipeWarden.Core.Backup;
using RecipeWarden.Infrastructure.Audit;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Settings;

namespace RecipeWarden.Infrastructure.Backup;

/// <summary>The running build's version ("0.7.0" or "0.7.0-dev.12"), shared by Web and CLI.</summary>
public static class RuntimeInfo
{
    public static string AppVersion { get; } = FromAssembly(typeof(RuntimeInfo).Assembly);

    public static string FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? informational[..plus] : informational;
    }
}

/// <summary>One backup or restore at a time, process-wide (a second request waits or is told to try later).</summary>
public sealed class BackupGate : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<IDisposable?> TryEnterAsync(TimeSpan wait, CancellationToken cancellationToken)
    {
        if (!await gate.WaitAsync(wait, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new Releaser(gate);
    }

    public void Dispose() => gate.Dispose();

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? gate = gate;

        public void Dispose()
        {
            gate?.Release();
            gate = null;
        }
    }
}

/// <summary>BR-63: while a restore runs the site shows a maintenance page and writes are blocked.</summary>
public sealed class MaintenanceMode
{
    private volatile string? reason;

    public bool IsActive => reason is not null;

    public string? Reason => reason;

    public IDisposable Enter(string why)
    {
        reason = why;
        return new Exit(this);
    }

    private sealed class Exit(MaintenanceMode owner) : IDisposable
    {
        private MaintenanceMode? owner = owner;

        public void Dispose()
        {
            if (owner is not null)
            {
                owner.reason = null;
            }

            owner = null;
        }
    }
}

public sealed record BackupRequest(BackupKind Kind, string Folder, int? ScheduleId = null);

public sealed record BackupOutcome(BackupRecord Record, string? Error)
{
    public bool Ok => Error is null;
}

/// <summary>
/// FR-BKP-01, 05, 12, 13, 15, BR-60, BR-61, section 9.6: a backup is a consistent snapshot. The database is copied
/// with SQLite's online backup API (not a file copy) and checked with integrity_check; media, the Data Protection key
/// ring (ADR 0038) and the config files are added with a manifest of SHA-256s into a .tar.gz, which is encrypted when
/// a key pair exists (ADR 0039). The destination must hold its marker file; the archive is written under a hidden
/// ".partial" name and renamed when complete. Every run is recorded; scheduled runs then apply retention to the
/// schedule's own archives only.
/// </summary>
public sealed partial class BackupService(
    AppDbContext db,
    DataOptions data,
    BackupOptions options,
    BackupFolders folders,
    SettingsStore settings,
    AuditLog audit,
    IAuditActor actor,
    TimeProvider clock,
    ILogger<BackupService> logger)
{
    public async Task<BackupOutcome> RunAsync(BackupRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var started = clock.GetUtcNow();
        var record = new BackupRecord
        {
            Kind = request.Kind,
            ScheduleId = request.ScheduleId,
            StartedAt = started,
            DestinationFolder = request.Folder,
            Status = BackupStatus.Running,
            StartedBy = AuditRules(actor.Current.UserName),
        };
        db.BackupRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        string? error;
        try
        {
            error = await WriteAsync(request, record, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or InvalidOperationException or CryptographicException)
        {
            LogFailed(logger, request.Folder, ex);
            error = ex is IOException or UnauthorizedAccessException ? BackupFolders.Fail(request.Folder, ex).Message : ex.Message;
        }

        record.CompletedAt = clock.GetUtcNow();
        record.Status = error is null ? BackupStatus.Succeeded : BackupStatus.Failed;
        record.Error = error is null ? null : Core.Audit.AuditRules.Truncate(error, 2000);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(error is null ? "Backup.Run" : "Backup.Failed", "BackupRecord", record.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new { kind = request.Kind.ToString(), folder = request.Folder, file = record.FileName, bytes = record.Bytes, encrypted = record.Encrypted, error },
            cancellationToken).ConfigureAwait(false);

        if (error is null && request.ScheduleId is { } scheduleId)
        {
            await ApplyRetentionAsync(scheduleId, cancellationToken).ConfigureAwait(false);
        }

        return new BackupOutcome(record, error);
    }

    private static string AuditRules(string name) => Core.Audit.AuditRules.Truncate(name, 256);

    private async Task<string?> WriteAsync(BackupRequest request, BackupRecord record, CancellationToken ct)
    {
        // SEC-74: the folder must still be an allowed folder at run time.
        if (folders.Resolve(request.Folder) is null)
        {
            return $"{request.Folder} is no longer in Backup:AllowedFolders.";
        }

        var marker = folders.CheckMarker(request.Folder);
        if (!marker.Ok)
        {
            return marker.Message; // FR-BKP-12: nothing is written
        }

        var publicKey = await settings.GetAsync(SettingKeys.BackupPublicKey, ct).ConfigureAwait(false);
        var work = Directory.CreateDirectory(Path.Combine(data.WorkDirectory, "backup-" + Guid.NewGuid().ToString("N"))).FullName;
        string? partial = null;
        try
        {
            var snapshot = Path.Combine(work, "recipewarden.db");
            var schema = SnapshotDatabase(snapshot);

            var now = clock.GetUtcNow();
            var fileName = UniqueName(request.Folder, BackupDestinationRules.ArchiveName(now.ToLocalTime(), encrypted: publicKey is not null));
            var finalPath = Path.Combine(request.Folder, fileName);
            partial = Path.Combine(request.Folder, BackupDestinationRules.PartialName(fileName));

            var files = new List<(string Entry, string Source)> { (ArchiveRules.DatabaseEntry, snapshot) };
            files.AddRange(Tree(data.MediaDirectory, "media/"));
            files.AddRange(Tree(data.KeysDirectory, "keys/").Where(f => f.Entry.EndsWith(".xml", StringComparison.Ordinal)));
            foreach (var config in options.ConfigFiles.Where(File.Exists))
            {
                files.Add(("config/" + Path.GetFileName(config), config));
            }

            var manifestFiles = new List<ManifestFile>();
            foreach (var (entry, source) in files)
            {
                manifestFiles.Add(new ManifestFile(entry, new FileInfo(source).Length, await Sha256Async(source, ct).ConfigureAwait(false)));
            }

            var manifest = new BackupManifest(BackupManifest.FormatName, BackupManifest.CurrentFormatVersion, RuntimeInfo.AppVersion, schema, now,
                request.Kind.ToString(), manifestFiles);

            var fileOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Options = FileOptions.Asynchronous,
            };
            if (!OperatingSystem.IsWindows())
            {
                fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; // SEC-71: backups 0600
            }

            string sha;
            long bytes;
            var file = new FileStream(partial, fileOptions);
            await using (file.ConfigureAwait(false))
            {
                var hashing = new HashingWriteStream(file);
                var encrypted = publicKey is null ? null : BackupCrypto.Encrypt(hashing, publicKey, leaveOpen: true);
                var gzip = new GZipStream(encrypted ?? (Stream)hashing, CompressionLevel.Fastest, leaveOpen: true);
                var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true);
                await using (tar.ConfigureAwait(false))
                {
                    foreach (var (entry, source) in files)
                    {
                        await tar.WriteEntryAsync(source, entry, ct).ConfigureAwait(false);
                    }

                    var json = JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJson.Options);
                    using var ms = new MemoryStream(json);
                    var manifestEntry = new PaxTarEntry(TarEntryType.RegularFile, BackupManifest.FileName) { DataStream = ms };
                    await tar.WriteEntryAsync(manifestEntry, ct).ConfigureAwait(false);
                }

                await gzip.DisposeAsync().ConfigureAwait(false);
                if (encrypted is not null)
                {
                    await encrypted.DisposeAsync().ConfigureAwait(false);
                }

                await file.FlushAsync(ct).ConfigureAwait(false);
                file.Flush(flushToDisk: true);
                sha = hashing.HashHex();
                bytes = hashing.BytesWritten;
            }

            File.Move(partial, finalPath); // FR-BKP-13: the final name appears only when the archive is complete
            partial = null;

            record.FileName = fileName;
            record.Bytes = bytes;
            record.Sha256 = sha;
            record.Encrypted = publicKey is not null;
            record.KeyFingerprint = publicKey is null ? null : await settings.GetAsync(SettingKeys.BackupKeyFingerprint, ct).ConfigureAwait(false);
            return null;
        }
        finally
        {
            if (partial is not null)
            {
                TryDelete(partial); // FR-BKP-13: no partial file is left behind
            }

            TryDeleteDirectory(work);
        }
    }

    /// <summary>Two runs in the same second (e.g. a pre-restore backup right after a manual one) get "-1", "-2"…</summary>
    private static string UniqueName(string folder, string name)
    {
        var candidate = name;
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        for (var i = 1; File.Exists(Path.Combine(folder, candidate)) || File.Exists(Path.Combine(folder, BackupDestinationRules.PartialName(candidate))); i++)
        {
            candidate = name[..dot] + "-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + name[dot..];
        }

        return candidate;
    }

    /// <summary>SQLite online backup into <paramref name="target"/>, as one file, checked with integrity_check. Returns the schema version.</summary>
    internal string SnapshotDatabase(string target)
    {
        using (var source = new SqliteConnection(data.ConnectionString))
        using (var dest = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target, Pooling = false }.ToString()))
        {
            source.Open();
            dest.Open();
            source.BackupDatabase(dest);
            Pragma(dest, SqlitePragma.JournalDelete);
            var integrity = Pragma(dest, SqlitePragma.IntegrityCheck);
            if (!string.Equals(integrity, "ok", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The database copy failed its integrity check: " + integrity);
            }
        }

        return ReadSchemaVersion(target);
    }

    internal static string ReadSchemaVersion(string databasePath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1;";
        return cmd.ExecuteScalar() as string ?? throw new InvalidOperationException("The database has no migration history.");
    }

    /// <summary>Runs one of a fixed set of statements (no text from outside ever reaches SQLite, SEC-20).</summary>
    internal static string? Pragma(SqliteConnection connection, SqlitePragma pragma)
    {
        using var cmd = connection.CreateCommand();
        switch (pragma)
        {
            case SqlitePragma.JournalDelete:
                cmd.CommandText = "PRAGMA journal_mode=DELETE;";
                break;
            case SqlitePragma.IntegrityCheck:
                cmd.CommandText = "PRAGMA integrity_check;";
                break;
            case SqlitePragma.QuickCheck:
                cmd.CommandText = "PRAGMA quick_check;";
                break;
            case SqlitePragma.PageSize:
                cmd.CommandText = "PRAGMA page_size;";
                break;
            case SqlitePragma.PageCount:
                cmd.CommandText = "PRAGMA page_count;";
                break;
            case SqlitePragma.FreelistCount:
                cmd.CommandText = "PRAGMA freelist_count;";
                break;
            case SqlitePragma.Vacuum:
                cmd.CommandText = "VACUUM;";
                break;
            case SqlitePragma.Checkpoint:
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                break;
            case SqlitePragma.CountRecipes:
                cmd.CommandText = "SELECT COUNT(*) FROM Recipes WHERE DeletedAt IS NULL;";
                break;
            case SqlitePragma.CountUsers:
                cmd.CommandText = "SELECT COUNT(*) FROM AspNetUsers;";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(pragma));
        }

        return cmd.ExecuteScalar()?.ToString();
    }

    /// <summary>
    /// BR-61, FR-BKP-03: deletes the schedule's own successful archives that its policy no longer keeps. Only files the
    /// schedule's runs recorded, in the folder they were recorded in, by their recorded name.
    /// </summary>
    public async Task<int> ApplyRetentionAsync(int scheduleId, CancellationToken cancellationToken = default)
    {
        var schedule = await db.BackupSchedules.FindAsync([scheduleId], cancellationToken).ConfigureAwait(false);
        if (schedule is null)
        {
            return 0;
        }

        var own = await db.BackupRecords
            .Where(r => r.ScheduleId == scheduleId && r.Status == BackupStatus.Succeeded && r.DeletedAt == null && r.FileName != null)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        own = own.Where(r => r.DestinationFolder == schedule.DestinationFolder).ToList();
        var now = clock.GetUtcNow();
        var delete = RetentionRules.SelectForDeletion(
            own.Select(r => new RetainedArchive(r.Id, (r.CompletedAt ?? r.StartedAt).ToLocalTime(), r.Kind)), schedule.Retention, now);

        var removed = 0;
        foreach (var record in own.Where(r => delete.Contains(r.Id)))
        {
            if (TryDelete(record.FullPath!))
            {
                record.DeletedAt = now;
                removed++;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (removed > 0)
        {
            await audit.RecordAsync("Backup.Retention", "BackupSchedule", scheduleId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                new { deleted = removed }, cancellationToken).ConfigureAwait(false);
        }

        return removed;
    }

    /// <summary>An Admin deletes one archive. Pre-restore and pre-upgrade archives younger than 30 days are kept (BR-61).</summary>
    public async Task<string?> DeleteArchiveAsync(int recordId, CancellationToken cancellationToken = default)
    {
        var record = await db.BackupRecords.FindAsync([recordId], cancellationToken).ConfigureAwait(false);
        if (record is null || record.FileName is null || record.DeletedAt is not null)
        {
            return "That archive is not listed.";
        }

        if (RetentionRules.IsProtected(record.Kind, record.CompletedAt ?? record.StartedAt, clock.GetUtcNow()))
        {
            return "Safety backups made before a restore or an upgrade are kept for 30 days.";
        }

        TryDelete(record.FullPath!);
        record.DeletedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return null;
    }

    /// <summary>Files under <paramref name="root"/> as (archive entry, source path), sorted for stable archives.</summary>
    internal static IEnumerable<(string Entry, string Source)> Tree(string root, string prefix)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !new FileInfo(f).Attributes.HasFlag(FileAttributes.ReparsePoint))
            .Select(f => (prefix + Path.GetRelativePath(root, f).Replace('\\', '/'), f))
            .OrderBy(f => f.Item1, StringComparer.Ordinal);
    }

    internal static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        var stream = File.OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
        }
    }

    public static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best effort; the work folder is under <data>/tmp
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Backup to {Folder} failed")]
    private static partial void LogFailed(ILogger logger, string folder, Exception exception);
}

/// <summary>The fixed statements the backup and health code run on SQLite.</summary>
internal enum SqlitePragma
{
    JournalDelete,
    IntegrityCheck,
    QuickCheck,
    PageSize,
    PageCount,
    FreelistCount,
    Vacuum,
    Checkpoint,
    CountRecipes,
    CountUsers,
}

/// <summary>JSON settings for manifest.json (camelCase, readable).</summary>
public static class ManifestJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}

/// <summary>Counts and hashes everything written through it (the archive's SHA-256 in the run history).</summary>
internal sealed class HashingWriteStream(Stream inner) : Stream
{
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public long BytesWritten { get; private set; }

    public string HashHex() => Convert.ToHexStringLower(hash.GetHashAndReset());

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => BytesWritten;
    public override long Position { get => BytesWritten; set => throw new NotSupportedException(); }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        hash.AppendData(buffer);
        BytesWritten += buffer.Length;
        inner.Write(buffer);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        hash.AppendData(buffer.Span);
        BytesWritten += buffer.Length;
        await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            hash.Dispose();
        }

        base.Dispose(disposing);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
