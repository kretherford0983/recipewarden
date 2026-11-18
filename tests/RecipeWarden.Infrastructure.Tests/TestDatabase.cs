using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Infrastructure;
using RecipeWarden.Infrastructure.Data;

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

    public static async Task<TestDatabase> CreateAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rw-infra-" + Guid.NewGuid().ToString("N"));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Data:Directory"] = dir })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRecipeWardenData(config);
        services.AddRecipeWardenIdentityCore();
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
