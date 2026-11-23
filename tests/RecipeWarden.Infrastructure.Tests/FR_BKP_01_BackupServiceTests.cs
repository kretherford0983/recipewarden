using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Backup;
using RecipeWarden.Infrastructure.Backup;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Tests.Shared;

namespace RecipeWarden.Infrastructure.Tests;

[Trait("Req", "FR-BKP-01")]
[Trait("Req", "BR-60")]
public sealed class FR_BKP_01_BackupServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Req", "AC-80")]
    public async Task Back_up_now_writes_database_media_keys_and_a_valid_manifest_and_records_size_and_checksum()
    {
        await using var s = await BackupTestScope.CreateAsync();
        await s.AddRecipeAsync("Chili");
        Directory.CreateDirectory(Path.Combine(s.Data.MediaDirectory, "ab"));
        await File.WriteAllBytesAsync(Path.Combine(s.Data.MediaDirectory, "ab", "photo.webp"), [1, 2, 3], Ct);

        var outcome = await s.BackupAsync();

        outcome.Error.ShouldBeNull();
        var record = outcome.Record;
        record.Status.ShouldBe(BackupStatus.Succeeded);
        record.Encrypted.ShouldBeFalse();
        record.FileName.ShouldNotBeNull().ShouldMatch(@"^backup-\d{8}-\d{6}\.tar\.gz$");
        var path = record.FullPath!;
        new FileInfo(path).Length.ShouldBe(record.Bytes);
        Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path, Ct))).ShouldBe(record.Sha256);
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(path).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite); // SEC-71
        }

        var entries = await BackupTestScope.ReadArchiveAsync(path);
        entries.Keys.ShouldContain(ArchiveRules.DatabaseEntry);
        entries.Keys.ShouldContain("media/ab/photo.webp");
        var manifest = JsonSerializer.Deserialize<BackupManifest>(entries[BackupManifest.FileName], ManifestJson.Options)!;
        ArchiveRules.CheckManifest(manifest).ShouldBeNull();
        manifest.SchemaVersion.ShouldBe(await s.WithAsync(sp => Task.FromResult(sp.GetRequiredService<AppDbContext>().Database.GetMigrations().Last())));
        foreach (var f in manifest.Files)
        {
            Convert.ToHexStringLower(SHA256.HashData(entries[f.Path])).ShouldBe(f.Sha256, f.Path);
        }

        Directory.GetFiles(s.Data.BackupsDirectory, "*.partial", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Req", "AC-82")]
    [Trait("Req", "FR-BKP-04")]
    public async Task Restore_takes_a_pre_restore_backup_and_the_data_matches_the_archive()
    {
        await using var s = await BackupTestScope.CreateAsync();
        await s.AddRecipeAsync("Chili");
        Directory.CreateDirectory(Path.Combine(s.Data.MediaDirectory, "ab"));
        await File.WriteAllBytesAsync(Path.Combine(s.Data.MediaDirectory, "ab", "old.webp"), [7], Ct);
        var archive = (await s.BackupAsync()).Record.FullPath!;

        await s.AddRecipeAsync("Pancakes");
        File.Delete(Path.Combine(s.Data.MediaDirectory, "ab", "old.webp"));
        await File.WriteAllBytesAsync(Path.Combine(s.Data.MediaDirectory, "ab", "new.webp"), [8], Ct);

        var error = await s.RestoreAsync(archive);
        error.ShouldBeNull(error);

        (await s.TitlesAsync()).ShouldContain("Chili");
        (await s.TitlesAsync()).ShouldNotContain("Pancakes");
        File.Exists(Path.Combine(s.Data.MediaDirectory, "ab", "old.webp")).ShouldBeTrue();
        File.Exists(Path.Combine(s.Data.MediaDirectory, "ab", "new.webp")).ShouldBeFalse();
        var records = await s.WithAsync(sp => sp.GetRequiredService<AppDbContext>().BackupRecords.ToListAsync(Ct));
        records.ShouldContain(r => r.Kind == BackupKind.PreRestore && r.Status == BackupStatus.Succeeded, "the pre-restore backup stays listed after the restore");
        var preRestore = Directory.GetFiles(s.Data.BackupsDirectory, "backup-*.tar.gz");
        preRestore.Length.ShouldBe(2, "the original archive and the pre-restore archive");
        var audit = await s.WithAsync(sp => sp.GetRequiredService<AppDbContext>().AuditEntries.Select(a => a.Action).ToListAsync(Ct));
        audit.ShouldContain("Restore.Run");
    }

    [Fact]
    [Trait("Req", "AC-82")]
    [Trait("Req", "SEC-54")]
    public async Task Archive_with_a_dot_dot_path_is_rejected_before_anything_changes()
    {
        await using var s = await BackupTestScope.CreateAsync();
        await s.AddRecipeAsync("Chili");
        var evil = Path.Combine(s.Nas, "evil.tar.gz");
        await BackupTestScope.WriteArchiveAsync(evil, [("media/../../escaped.txt", "x"u8.ToArray(), TarEntryType.RegularFile)]);

        var result = await s.PrepareAsync(evil);

        result.Error.ShouldNotBeNull().ShouldContain("..");
        File.Exists(Path.Combine(s.Data.Directory, "escaped.txt")).ShouldBeFalse();
        Directory.GetFiles(Path.GetDirectoryName(s.Data.Directory)!, "escaped.txt", SearchOption.AllDirectories).ShouldBeEmpty();
        (await s.TitlesAsync()).ShouldContain("Chili");
        Directory.EnumerateDirectories(s.Data.WorkDirectory).ShouldBeEmpty("the quarantine folder is removed");
    }

    [Theory]
    [InlineData("/etc/cron.d/evil", "absolute")]
    [InlineData("random.txt", "not part")]
    [Trait("Req", "SEC-54")]
    public async Task Foreign_or_absolute_entries_are_rejected(string name, string fragment)
    {
        await using var s = await BackupTestScope.CreateAsync();
        var evil = Path.Combine(s.Nas, "evil.tar.gz");
        await BackupTestScope.WriteArchiveAsync(evil, [(name, "x"u8.ToArray(), TarEntryType.RegularFile)]);

        (await s.PrepareAsync(evil)).Error.ShouldNotBeNull().ShouldContain(fragment);
    }

    [Fact]
    [Trait("Req", "SEC-54")]
    public async Task Links_are_rejected()
    {
        await using var s = await BackupTestScope.CreateAsync();
        var evil = Path.Combine(s.Nas, "link.tar.gz");
        await BackupTestScope.WriteArchiveAsync(evil, [("media/x", [], TarEntryType.SymbolicLink)]);

        (await s.PrepareAsync(evil)).Error.ShouldNotBeNull().ShouldContain("link");
    }

    [Fact]
    [Trait("Req", "SEC-54")]
    public async Task Size_and_count_caps_apply_before_extraction()
    {
        await using var s = await BackupTestScope.CreateAsync();
        var archive = (await s.BackupAsync()).Record.FullPath!;

        (await s.PrepareAsync(archive, limits: new ArchiveLimits(10, 10, 100))).Error.ShouldNotBeNull().ShouldContain("limit");
        (await s.PrepareAsync(archive, limits: new ArchiveLimits(long.MaxValue, long.MaxValue, 1))).Error.ShouldNotBeNull().ShouldContain("too many");
    }

    [Fact]
    [Trait("Req", "AC-82")]
    [Trait("Req", "BR-62")]
    public async Task Archive_with_a_changed_database_fails_the_checksum_and_nothing_changes()
    {
        await using var s = await BackupTestScope.CreateAsync();
        await s.AddRecipeAsync("Chili");
        var archive = (await s.BackupAsync()).Record.FullPath!;
        var entries = await BackupTestScope.ReadArchiveAsync(archive);
        var db = entries[ArchiveRules.DatabaseEntry];
        db[^1] ^= 0xFF; // flip a byte, keep the old manifest
        var tampered = Path.Combine(s.Nas, "tampered.tar.gz");
        await BackupTestScope.WriteArchiveAsync(tampered, entries.Select(e => (e.Key, e.Value, TarEntryType.RegularFile)));

        (await s.PrepareAsync(tampered)).Error.ShouldBe(RestoreService.ChecksumMismatch);
        (await s.TitlesAsync()).ShouldContain("Chili");
    }

    [Fact]
    [Trait("Req", "BR-62")]
    public async Task Unlisted_files_and_missing_manifests_are_rejected()
    {
        await using var s = await BackupTestScope.CreateAsync();
        var archive = (await s.BackupAsync()).Record.FullPath!;
        var entries = await BackupTestScope.ReadArchiveAsync(archive);

        var extra = Path.Combine(s.Nas, "extra.tar.gz");
        await BackupTestScope.WriteArchiveAsync(extra, entries.Select(e => (e.Key, e.Value, TarEntryType.RegularFile))
            .Append(("media/smuggled.webp", new byte[] { 1 }, TarEntryType.RegularFile)));
        (await s.PrepareAsync(extra)).Error.ShouldNotBeNull().ShouldContain("not in its manifest");

        var noManifest = Path.Combine(s.Nas, "nomanifest.tar.gz");
        await BackupTestScope.WriteArchiveAsync(noManifest, entries.Where(e => e.Key != BackupManifest.FileName).Select(e => (e.Key, e.Value, TarEntryType.RegularFile)));
        (await s.PrepareAsync(noManifest)).Error.ShouldNotBeNull().ShouldContain("manifest");

        var notAnArchive = Path.Combine(s.Nas, "notes.txt");
        await File.WriteAllTextAsync(notAnArchive, "hello", Ct);
        (await s.PrepareAsync(notAnArchive)).Error.ShouldNotBeNull().ShouldContain("not a RecipeWarden backup");
    }

    [Fact]
    [Trait("Req", "BR-65")]
    [Trait("Req", "AC-85")]
    public async Task Archive_from_a_newer_version_is_refused_before_any_change()
    {
        await using var s = await BackupTestScope.CreateAsync();
        var archive = (await s.BackupAsync()).Record.FullPath!;
        var entries = await BackupTestScope.ReadArchiveAsync(archive);
        var manifest = JsonSerializer.Deserialize<BackupManifest>(entries[BackupManifest.FileName], ManifestJson.Options)! with { AppVersion = "99.0.0" };
        entries[BackupManifest.FileName] = JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJson.Options);
        var newer = Path.Combine(s.Nas, "newer.tar.gz");
        await BackupTestScope.WriteArchiveAsync(newer, entries.Select(e => (e.Key, e.Value, TarEntryType.RegularFile)));

        (await s.PrepareAsync(newer)).Error.ShouldNotBeNull().ShouldContain("Install version 99.0.0 or later, then restore");
    }

    [Fact]
    [Trait("Req", "FR-BKP-15")]
    [Trait("Req", "AC-89")]
    [Trait("Req", "SEC-75")]
    public async Task Encrypted_archives_need_the_kit_and_passphrase_and_a_wrong_passphrase_changes_nothing()
    {
        await using var s = await BackupTestScope.CreateAsync();
        var (kit, fingerprint) = await s.CreateKeyAsync("correct horse battery");
        await s.AddRecipeAsync("Chili");
        var outcome = await s.BackupAsync(folder: s.Nas);
        outcome.Error.ShouldBeNull();
        outcome.Record.Encrypted.ShouldBeTrue();
        outcome.Record.KeyFingerprint.ShouldBe(fingerprint);
        outcome.Record.FileName.ShouldNotBeNull().ShouldEndWith(".tar.gz.rwenc");
        var archive = outcome.Record.FullPath!;
        var head = (await File.ReadAllBytesAsync(archive, Ct))[..6];
        BackupCrypto.IsEncrypted(head).ShouldBeTrue();
        (await File.ReadAllTextAsync(archive, Ct)).ShouldNotContain("Chili");
        kit.ShouldContain(fingerprint);
        kit.ShouldContain(BackupCrypto.KitBegin);
        (await s.WithAsync(sp => sp.Settings().GetAsync(Settings.SettingKeys.BackupPublicKey, Ct))).ShouldNotBeNull().ShouldNotContain("PRIVATE");

        await s.AddRecipeAsync("Pancakes");
        (await s.PrepareAsync(archive)).Error.ShouldNotBeNull().ShouldContain("encrypted");
        (await s.PrepareAsync(archive, new RecoveryKey(kit, "wrong horse battery"))).Error.ShouldBe(BackupCrypto.WrongKit);
        (await s.TitlesAsync()).ShouldContain("Pancakes", "a wrong passphrase changes nothing");

        (await s.RestoreAsync(archive, new RecoveryKey(kit, "correct horse battery"))).ShouldBeNull();
        (await s.TitlesAsync()).ShouldNotContain("Pancakes");
    }

    [Fact]
    [Trait("Req", "FR-BKP-15")]
    public async Task A_kit_for_another_key_is_named_as_such()
    {
        await using var s = await BackupTestScope.CreateAsync();
        var (oldKit, _) = await s.CreateKeyAsync("first passphrase!!");
        await s.CreateKeyAsync("second passphrase!");
        var archive = (await s.BackupAsync()).Record.FullPath!;

        (await s.PrepareAsync(archive, new RecoveryKey(oldKit, "first passphrase!!"))).Error.ShouldNotBeNull().ShouldContain("different recovery kit");
        (await s.PrepareAsync(archive, new RecoveryKey("not a kit", "x"))).Error.ShouldNotBeNull().ShouldContain("not a RecipeWarden recovery kit");
    }

    [Fact]
    [Trait("Req", "FR-BKP-15")]
    public async Task Changing_one_encrypted_byte_or_cutting_the_file_is_detected()
    {
        await using var s = await BackupTestScope.CreateAsync();
        var (kit, _) = await s.CreateKeyAsync("correct horse battery");
        var archive = (await s.BackupAsync()).Record.FullPath!;
        var bytes = await File.ReadAllBytesAsync(archive, Ct);

        var flipped = (byte[])bytes.Clone();
        flipped[^20] ^= 1;
        var changed = Path.Combine(s.Nas, "changed.rwenc");
        await File.WriteAllBytesAsync(changed, flipped, Ct);
        (await s.PrepareAsync(changed, new RecoveryKey(kit, "correct horse battery"))).Error.ShouldNotBeNull().ShouldContain("changed or damaged");

        var cut = Path.Combine(s.Nas, "cut.rwenc");
        await File.WriteAllBytesAsync(cut, bytes[..(bytes.Length / 2)], Ct);
        (await s.PrepareAsync(cut, new RecoveryKey(kit, "correct horse battery"))).Error.ShouldNotBeNull();
    }

    [Fact]
    [Trait("Req", "FR-BKP-15")]
    public void Encryption_round_trips_across_chunk_boundaries()
    {
        var pair = BackupCrypto.CreateKeyPair("correct horse battery", DateTimeOffset.UnixEpoch, iterations: 1000);
        var data = RandomNumberGenerator.GetBytes(BackupCrypto.ChunkSize * 2 + 123);
        using var encrypted = new MemoryStream();
        using (var e = BackupCrypto.Encrypt(encrypted, pair.PublicKeyPem, leaveOpen: true))
        {
            e.Write(data);
        }

        encrypted.Position = 0;
        using var rsa = BackupCrypto.OpenKit(pair.RecoveryKit, "correct horse battery");
        using var d = BackupCrypto.Decrypt(encrypted, rsa);
        using var back = new MemoryStream();
        d.CopyTo(back);
        back.ToArray().ShouldBe(data);
        Encoding.UTF8.GetString(encrypted.ToArray()).ShouldNotContain(pair.Fingerprint + "PRIVATE");
    }

    [Fact]
    [Trait("Req", "FR-BKP-12")]
    [Trait("Req", "AC-88")]
    public async Task With_the_marker_missing_the_run_fails_and_nothing_is_written()
    {
        await using var s = await BackupTestScope.CreateAsync(nasMarker: false);

        var outcome = await s.BackupAsync(folder: s.Nas);

        outcome.Error.ShouldNotBeNull().ShouldContain(BackupDestinationRules.NotMounted);
        outcome.Record.Status.ShouldBe(BackupStatus.Failed);
        Directory.EnumerateFileSystemEntries(s.Nas).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Req", "FR-BKP-13")]
    [Trait("Req", "AC-88")]
    public async Task An_interrupted_backup_leaves_no_file_with_the_final_name_and_no_partial_file()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.Zero));
        await using var s = await BackupTestScope.CreateAsync(clock);
        var name = BackupDestinationRules.ArchiveName(clock.GetUtcNow().ToLocalTime(), encrypted: false);
        Directory.CreateDirectory(Path.Combine(s.Nas, name)); // the rename at the end cannot happen

        var outcome = await s.BackupAsync(folder: s.Nas);

        outcome.Error.ShouldNotBeNull();
        File.Exists(Path.Combine(s.Nas, name)).ShouldBeFalse();
        Directory.GetFiles(s.Nas, "*.partial").ShouldBeEmpty();
    }

    [Fact]
    [Trait("Req", "SEC-74")]
    public async Task A_folder_that_is_not_allowed_is_refused_at_run_time()
    {
        await using var s = await BackupTestScope.CreateAsync();
        var other = Path.Combine(Path.GetDirectoryName(s.Nas)!, "elsewhere");
        Directory.CreateDirectory(other);
        await File.WriteAllTextAsync(Path.Combine(other, BackupDestinationRules.MarkerFileName), "", Ct);

        (await s.BackupAsync(folder: other)).Error.ShouldNotBeNull().ShouldContain("no longer in Backup:AllowedFolders");
        Directory.GetFiles(other, "backup-*").ShouldBeEmpty();
    }

    [Fact]
    [Trait("Req", "FR-BKP-11")]
    [Trait("Req", "AC-87")]
    public async Task Test_on_a_writable_folder_succeeds_and_leaves_no_probe()
    {
        await using var s = await BackupTestScope.CreateAsync();
        var folders = s.Services.GetRequiredService<BackupFolders>();

        folders.Allowed().ShouldBe([BackupFolderNormal(s.Data.BackupsDirectory), s.Nas]);
        folders.Test(s.Nas).Ok.ShouldBeTrue();
        Directory.GetFiles(s.Nas, BackupDestinationRules.ProbePrefix + "*").ShouldBeEmpty();
        folders.Test(s.Data.BackupsDirectory).Ok.ShouldBeTrue("the built-in folder keeps its own marker");
    }

    [Theory]
    [InlineData("Read-only file system : '/mnt/nas/x'", FolderProblem.ReadOnly, "apply-config")]
    [InlineData("No space left on device", FolderProblem.NoSpace, "retention")]
    [InlineData("Permission denied", FolderProblem.PermissionDenied, "owned by")]
    [InlineData("boom", FolderProblem.Other, "boom")]
    [Trait("Req", "FR-BKP-11")]
    [Trait("Req", "AC-87")]
    public void OS_errors_are_reported_with_the_hint(string message, FolderProblem problem, string hint)
    {
        var check = BackupFolders.Fail("/mnt/nas", new IOException(message));
        check.Problem.ShouldBe(problem);
        check.Message.ShouldContain(hint);
        BackupFolders.Classify(new UnauthorizedAccessException("Access denied")).ShouldBe(FolderProblem.PermissionDenied);
        BackupFolders.Classify(new DirectoryNotFoundException()).ShouldBe(FolderProblem.NotFound);
    }

    private static string BackupFolderNormal(string path) => BackupFolderPolicy.Normalize(path);
}
