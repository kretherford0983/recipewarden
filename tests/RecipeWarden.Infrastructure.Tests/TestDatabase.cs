using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Infrastructure;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Tests.Shared;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>A real SQLite file in a temp directory, initialized exactly as at startup.</summary>
internal sealed class TestDatabase : IAsyncDisposable
{
    private TestDatabase(string directory, ServiceProvider provider)
    {
        Directory = directory;
        Provider = provider;
    }

    public string Directory { get; }

    public ServiceProvider Provider { get; }

    /// <param name="fromTemplate">Start from the migrated template (default) or from an empty directory.</param>
    /// <param name="clock">Replaces the system clock (BR-05 trash tests).</param>
    public static async Task<TestDatabase> CreateAsync(bool fromTemplate = true, TimeProvider? clock = null, Action<IServiceCollection>? configure = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "rw-infra-" + Guid.NewGuid().ToString("N"));
        if (fromTemplate)
        {
            TestDatabaseTemplate.CopyTo(dir);
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Data:Directory"] = dir })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRecipeWardenData(config);
        services.AddRecipeWardenIdentityCore();
        services.UseFastPasswordHashing();
        if (clock is not null)
        {
            services.AddSingleton(clock);
        }

        configure?.Invoke(services);
        var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(TestContext.Current.CancellationToken);
        }

        return new TestDatabase(dir, provider);
    }

    public async ValueTask DisposeAsync()
    {
        await Provider.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }
}
