using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RecipeWarden.Core.Import;
using RecipeWarden.Core.Python;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Import;
using RecipeWarden.Tests.Shared;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>
/// AC-109 (widened 2026-11-25, owner): "import-compare --url" fetches one page through SafeFetcher and compares the
/// .NET reading with the site reader's, so a site that failed to import can be tried. Nothing is saved.
/// </summary>
[Trait("Req", "AC-109")]
[Trait("Req", "FR-IMP-12")]
public sealed class AC_109_ImportCompareUrlTests
{
    private const string Url = "http://www.allrecipes.example/recipe/9/lentil-soup";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string NoSchemaPage = """
        <html><head><title>Lentil Soup</title></head><body><h1>Lentil Soup</h1>
        <h2>Ingredients</h2><ul><li>1 cup lentils</li><li>4 cups stock</li></ul><p>Comments are closed.</p></body></html>
        """;

    private static async Task<(TestDatabase Db, FakeWeb Web, ISiteRecipeScraper Scraper)> CreateAsync()
    {
        var web = new FakeWeb();
        var scraper = Substitute.For<ISiteRecipeScraper>();
        scraper.ScrapeAsync(Arg.Any<string>(), Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns(WorkerOutcome.Success(
            new SiteRecipeReading(true, true, "Lentil Soup", ["1 cup lentils", "4 cups stock"], ["Rinse.", "Simmer."],
                null, null, null, null, null, null, null, null), "recipe-scrapers 15.14.0"));
        var tdb = await TestDatabase.CreateAsync(configure: s =>
        {
            s.AddSingleton<IDnsResolver>(web);
            s.AddSingleton<ISocketConnector>(web);
            s.AddSingleton(scraper);
        });
        return (tdb, web, scraper);
    }

    [Fact]
    [Trait("Req", "AC-112")]
    public async Task A_given_page_is_fetched_once_and_compared_without_saving_anything()
    {
        var (tdb, web, scraper) = await CreateAsync();
        await using (tdb)
        await using (web)
        {
            web.Html(Url, NoSchemaPage);
            await using var scope = tdb.Provider.CreateAsyncScope();

            var report = await scope.ServiceProvider.GetRequiredService<ImportCompareService>().CompareUrlAsync(Url, Ct);

            report.FetchError.ShouldBeNull();
            report.Failure.ShouldBe(WorkerFailure.None);
            var row = report.Rows.ShouldHaveSingleItem();
            row.Url.ShouldBe(Url);
            row.DotNetComplete.ShouldBeFalse("the page has ingredients but no steps");
            row.SiteComplete.ShouldBeTrue();
            row.Verdict.ShouldBe(CompareVerdict.OnlySiteReader);
            await scraper.Received(1).ScrapeAsync(Arg.Is<string>(h => h.Contains("Comments are closed.", StringComparison.Ordinal)),
                Arg.Is<Uri>(u => u.AbsoluteUri == Url), Arg.Any<CancellationToken>());
            web.Requests.Count.ShouldBe(1);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.ImportJobs.CountAsync(Ct)).ShouldBe(0, "a comparison is not an import");
            Directory.Exists(scope.ServiceProvider.GetRequiredService<DataOptions>().ImportsDirectory)
                .ShouldBeFalse("nothing is written to the imports folder");
        }
    }

    [Fact]
    [Trait("Req", "SEC-41")]
    public async Task A_refused_or_failed_fetch_is_reported_and_the_site_reader_is_not_asked()
    {
        var (tdb, web, scraper) = await CreateAsync();
        await using (tdb)
        await using (web)
        {
            web.Serve(Url, 403, "text/html", "Forbidden"u8.ToArray());
            await using var scope = tdb.Provider.CreateAsyncScope();
            var compare = scope.ServiceProvider.GetRequiredService<ImportCompareService>();

            (await compare.CompareUrlAsync(Url, Ct)).FetchError.ShouldNotBeNullOrWhiteSpace();
            (await compare.CompareUrlAsync("http://127.0.0.1/recipe", Ct)).FetchError.ShouldNotBeNullOrWhiteSpace();
            (await compare.CompareUrlAsync("ftp://example.com/x", Ct)).FetchError.ShouldNotBeNullOrWhiteSpace();
            (await compare.CompareUrlAsync("not a url", Ct)).Rows.ShouldBeEmpty();

            await scraper.DidNotReceiveWithAnyArgs().ScrapeAsync(default!, default!, Ct);
            web.Connections.ShouldNotContain(System.Net.IPAddress.Loopback);
        }
    }
}
