using System.Formats.Tar;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Backup;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Audit;
using RecipeWarden.Infrastructure.Backup;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Health;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Infrastructure.Settings;
using RecipeWarden.Tests.Shared;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>A test database with a second allowed backup folder ("the NAS") and the M7 services.</summary>
internal sealed class BackupTestScope : IAsyncDisposable
{
    private readonly TestDatabase tdb;

    private BackupTestScope(TestDatabase tdb, string nas)
    {
        this.tdb = tdb;
        Nas = nas;
    }

    public string Nas { get; }

    public IServiceProvider Services => tdb.Provider;

    public DataOptions Data => tdb.Provider.GetRequiredService<DataOptions>();

    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<BackupTestScope> CreateAsync(TimeProvider? clock = null, Action<IServiceCollection>? configure = null, bool nasMarker = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "rw-bkp-" + Guid.NewGuid().ToString("N"));
        var nas = Path.Combine(root, "nas");
        Directory.CreateDirectory(nas);
        if (nasMarker)
        {
            await File.WriteAllTextAsync(Path.Combine(nas, BackupDestinationRules.MarkerFileName), "", Ct);
        }

        var tdb = await TestDatabase.CreateAsync(clock: clock, configure: configure, directory: Path.Combine(root, "data"),
            settings: new Dictionary<string, string?> { ["Backup:AllowedFolders:0"] = nas + "/" });
        return new BackupTestScope(tdb, nas);
    }

    public async Task<T> WithAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = tdb.Provider.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }

    public Task<BackupOutcome> BackupAsync(BackupKind kind = BackupKind.Manual, string? folder = null, int? scheduleId = null) =>
        WithAsync(sp => sp.GetRequiredService<BackupService>().RunAsync(new BackupRequest(kind, folder ?? Data.BackupsDirectory, scheduleId), Ct));

    public Task<int> AddRecipeAsync(string title) => WithAsync(async sp =>
        (await sp.GetRequiredService<RecipeService>().CreateAsync(new RecipeEdit
        {
            Title = title,
            IngredientsText = "1 cup flour",
            StepsText = "Mix.",
            WantsPublish = true,
        }, "seed", Ct)).Id);

    public Task<List<string>> TitlesAsync() => WithAsync(async sp =>
        await sp.GetRequiredService<AppDbContext>().Recipes.OrderBy(r => r.Title).Select(r => r.Title).ToListAsync(Ct));

    public Task<(string Kit, string Fingerprint)> CreateKeyAsync(string passphrase = "a long enough passphrase") => WithAsync(async sp =>
    {
        var keys = sp.GetRequiredService<BackupKeyService>();
        keys.KitIterations = 1000; // tests only; production uses 600,000
        var (kit, fp, error) = await keys.CreateAsync(passphrase, passphrase, replaceExisting: true, Ct);
        error.ShouldBeNull();
        return (kit!, fp!);
    });

    public Task<RestorePreparation> PrepareAsync(string archive, RecoveryKey? key = null, ArchiveLimits? limits = null) =>
        WithAsync(sp => sp.GetRequiredService<RestoreService>().PrepareAsync(archive, key, limits, Ct));

    public async Task<string?> RestoreAsync(string archive, RecoveryKey? key = null, bool preRestore = true)
    {
        var prepared = await PrepareAsync(archive, key);
        prepared.Error.ShouldBeNull(prepared.Error);
        using var p = prepared.Prepared!;
        return await WithAsync(sp => sp.GetRequiredService<RestoreService>().ApplyAsync(p, new RestoreOptions(preRestore, "test"), Ct));
    }

    /// <summary>Entry names and bytes of an unencrypted archive.</summary>
    public static async Task<Dictionary<string, byte[]>> ReadArchiveAsync(string path)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        await using var file = File.OpenRead(path);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        await using var reader = new TarReader(gzip);
        while (await reader.GetNextEntryAsync(copyData: true, Ct) is { } entry)
        {
            using var ms = new MemoryStream();
            if (entry.DataStream is not null)
            {
                await entry.DataStream.CopyToAsync(ms, Ct);
            }

            result[entry.Name] = ms.ToArray();
        }

        return result;
    }

    /// <summary>Writes a .tar.gz from (name, bytes, type) — used to build tampered archives.</summary>
    public static async Task WriteArchiveAsync(string path, IEnumerable<(string Name, byte[] Bytes, TarEntryType Type)> entries)
    {
        await using var file = File.Create(path);
        await using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        await using var writer = new TarWriter(gzip, TarEntryFormat.Pax);
        foreach (var (name, bytes, type) in entries)
        {
            var entry = new PaxTarEntry(type, name);
            if (type == TarEntryType.SymbolicLink)
            {
                entry.LinkName = "/etc/passwd";
            }
            else if (type == TarEntryType.RegularFile)
            {
                entry.DataStream = new MemoryStream(bytes);
            }

            await writer.WriteEntryAsync(entry, Ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        var root = Path.GetDirectoryName(tdb.Directory)!;
        await tdb.DisposeAsync();
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }
}

internal static class ServiceProviderExtensions
{
    public static AuditLog Audit(this IServiceProvider sp) => sp.GetRequiredService<AuditLog>();

    public static SettingsStore Settings(this IServiceProvider sp) => sp.GetRequiredService<SettingsStore>();

    public static HealthService Health(this IServiceProvider sp) => sp.GetRequiredService<HealthService>();
}
