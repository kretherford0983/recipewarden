using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Cli.Admin;
using RecipeWarden.Core.Import;
using RecipeWarden.Infrastructure;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Cli.Python;

namespace RecipeWarden.Cli.Tests;

/// <summary>AC-109: "recipewarden import-compare" with nothing saved yet says so and exits 0 without needing the worker.</summary>
[Trait("Req", "AC-109")]
public sealed class AC_109_ImportCompareCommandTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("rw-cli-compare-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IConfiguration Config(string? python = null) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Data:Directory"] = dir, ["Health:LogDirectory"] = Path.Combine(dir, "logs"),
            ["Python:Directory"] = python ?? "/nonexistent/rw-python",
            ["Python:NltkData"] = python is null ? null : Path.Combine(Root(), "python", ".nltk_data"),
        })
        .Build();

    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RecipeWarden.slnx")))
        {
            d = d.Parent;
        }

        return d!.FullName;
    }

    [Fact]
    public async Task Saved_imports_are_listed_with_a_verdict_and_a_summary()
    {
        var config = Config(Path.Combine(Root(), "python", ".venv"));
        (await AdminCreateCommand.RunAsync(config, "b@example.com", "correct horse battery", new StringWriter(), new StringWriter(), Ct)).ShouldBe(0);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRecipeWardenData(config);
        await using (var provider = services.BuildServiceProvider())
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var imports = scope.ServiceProvider.GetRequiredService<DataOptions>().ImportsDirectory;
            Directory.CreateDirectory(Path.Combine(imports, "b1"));
            File.Copy(Path.Combine(Root(), "tests", "RecipeWarden.Importers.Tests", "Fixtures", "Html", "jsonld-graph.html"), Path.Combine(imports, "b1", "0.raw"));
            db.ImportJobs.Add(new ImportJob
            {
                BatchId = "b1", SourceKind = ImportSourceKind.Url, SourceRef = "https://blog.example/soup", Status = ImportStatus.Review,
                RawPath = "b1/0.raw", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(Ct);
        }

        var output = new StringWriter();
        var error = new StringWriter();
        (await ImportCompareCommand.RunAsync(config, output, error, Ct)).ShouldBe(0, error.ToString());
        var text = output.ToString();
        text.ShouldMatch(@"BothComplete\s+JsonLd\s+no site reader\s+4 / 4\s+\d+ / \d+\s+https://blog\.example/soup");
        text.ShouldContain("1 URL imports compared (0 purged or unreadable); 0 from hosts with a site reader.");
        text.ShouldContain("M10 would not help yet.");

        // Without a worker the command stops and says why (BR-119).
        error = new StringWriter();
        (await ImportCompareCommand.RunAsync(Config(), new StringWriter(), error, Ct)).ShouldBe(1);
        error.ToString().ShouldContain("the site reader is not available (Unavailable");
    }

    [Fact]
    public async Task With_no_saved_url_imports_it_says_so()
    {
        // The database is created the way an install does it (the first Admin).
        (await AdminCreateCommand.RunAsync(Config(), "a@example.com", "correct horse battery", new StringWriter(), new StringWriter(), Ct)).ShouldBe(0);
        var output = new StringWriter();

        var code = await ImportCompareCommand.RunAsync(Config(), output, new StringWriter(), Ct);

        code.ShouldBe(0);
        output.ToString().ShouldContain("No saved URL imports to compare (0 purged or unreadable). Imports are kept 30 days.");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(dir, recursive: true);
    }
}
