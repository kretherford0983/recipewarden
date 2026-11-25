using Microsoft.Extensions.Configuration;
using RecipeWarden.Cli.Admin;
using RecipeWarden.Cli.Python;

namespace RecipeWarden.Cli.Tests;

/// <summary>AC-109 (widened 2026-11-25): "recipewarden import-compare --url" reports a page that cannot be fetched and why.</summary>
[Trait("Req", "AC-109")]
public sealed class AC_109_ImportCompareUrlCommandTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("rw-cli-compare-url-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Data:Directory"] = dir, ["Health:LogDirectory"] = Path.Combine(dir, "logs"), ["Python:Directory"] = "/nonexistent/rw-python",
        })
        .Build();

    [Fact]
    [Trait("Req", "SEC-41")]
    public async Task A_page_that_cannot_be_fetched_is_reported_with_the_reason_and_how_to_go_on()
    {
        (await AdminCreateCommand.RunAsync(Config(), "u@example.com", "correct horse battery", new StringWriter(), new StringWriter(), Ct)).ShouldBe(0);
        var output = new StringWriter();
        var error = new StringWriter();

        var code = await ImportCompareCommand.RunAsync(Config(), output, error, ["http://127.0.0.1/recipe", "ftp://example.com/x"], Ct);

        code.ShouldBe(1);
        var text = error.ToString();
        text.ShouldContain("http://127.0.0.1/recipe: ");
        text.ShouldContain("ftp://example.com/x: ");
        output.ToString().ShouldContain("save the page from your browser");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(dir, recursive: true);
    }
}
