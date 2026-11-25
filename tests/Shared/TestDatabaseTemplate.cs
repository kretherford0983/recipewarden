using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Infrastructure;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Tests.Shared;

/// <summary>
/// docs/testing.md (test speed): the database is migrated once per test process by the real
/// <see cref="DatabaseInitializer"/>, and each test or host gets a file copy of it instead of migrating its own.
/// </summary>
internal static class TestDatabaseTemplate
{
    private static readonly Lazy<string> Template = new(Create, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Copies the migrated template into <paramref name="dataDirectory"/> (as DataOptions expects it).</summary>
    public static void CopyTo(string dataDirectory)
    {
        var target = new DataOptions { Directory = dataDirectory }.DatabasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(Template.Value, target, overwrite: true);
    }

    /// <summary>
    /// Test hosts only: PBKDF2 with one iteration instead of the production default, so the many sign-ins in tests
    /// are cheap. The app's own hashing settings are unchanged.
    /// </summary>
    public static IServiceCollection UseFastPasswordHashing(this IServiceCollection services) =>
        services.Configure<PasswordHasherOptions>(o => o.IterationCount = 1);

    private static string Create()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rw-template-{Environment.ProcessId}-{Guid.NewGuid():N}");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Data:Directory"] = dir })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRecipeWardenData(config);
        services.AddRecipeWardenIdentityCore();

        using (var provider = services.BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            Task.Run(() => scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync()).GetAwaiter().GetResult();
        }

        var path = new DataOptions { Directory = dir }.DatabasePath;

        // Fold the WAL into the main file so a plain file copy holds the whole database.
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // best effort
            }
        };
        return path;
    }
}
