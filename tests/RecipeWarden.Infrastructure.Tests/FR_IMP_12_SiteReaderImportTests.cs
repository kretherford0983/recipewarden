using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RecipeWarden.Core.Import;
using RecipeWarden.Core.Python;
using RecipeWarden.Infrastructure.Import;
using RecipeWarden.Tests.Shared;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>
/// FR-IMP-12, BR-117, AC-110 to AC-112, AC-131, ADR 0059: the site reader's place in the URL and paste import order,
/// behind the Admin switch. The worker is a fake here; its own behaviour is covered by the Python tests and AC-109.
/// </summary>
[Trait("Req", "FR-IMP-12")]
public sealed class FR_IMP_12_SiteReaderImportTests
{
    private const string SupportedUrl = "http://www.allrecipes.example/recipe/42/lentil-soup";
    private const string UserId = "user-1";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string NoSchemaPage = """
        <html><head><title>Lentil Soup</title></head><body><h1>Lentil Soup</h1>
        <h2>Ingredients</h2><ul><li>1 cup lentils</li><li>4 cups stock</li></ul><p>Comments are closed.</p></body></html>
        """;

    private const string CompletePage = """
        <html><head><script type="application/ld+json">{"@type":"Recipe","name":"Lentil Soup",
          "recipeIngredient":["1 cup lentils","4 cups stock"],"recipeInstructions":["Simmer."]}</script></head><body></body></html>
        """;

    private sealed class Scope : IAsyncDisposable
    {
        private readonly TestDatabase tdb;

        private Scope(TestDatabase tdb, FakeWeb web, ISiteRecipeScraper scraper)
        {
            this.tdb = tdb;
            Web = web;
            Scraper = scraper;
        }

        public FakeWeb Web { get; }

        public ISiteRecipeScraper Scraper { get; }

        public static async Task<Scope> CreateAsync(bool siteReadersOn)
        {
            var web = new FakeWeb();
            var scraper = Substitute.For<ISiteRecipeScraper>();
            var tdb = await TestDatabase.CreateAsync(configure: s =>
            {
                s.AddSingleton<IDnsResolver>(web);
                s.AddSingleton<ISocketConnector>(web);
                s.AddSingleton(scraper);
            });
            var scope = new Scope(tdb, web, scraper);
            if (siteReadersOn)
            {
                await scope.WithAsync(async sp =>
                {
                    await sp.GetRequiredService<SiteReaderSettings>().SetEnabledAsync(true, Ct);
                    return true;
                });
            }

            return scope;
        }

        public async Task<T> WithAsync<T>(Func<IServiceProvider, Task<T>> work)
        {
            await using var scope = tdb.Provider.CreateAsyncScope();
            return await work(scope.ServiceProvider);
        }

        public Task<T> Import<T>(Func<ImportService, Task<T>> work) => WithAsync(sp => work(sp.GetRequiredService<ImportService>()));

        public async Task<ImportReview> ReviewAsync(ImportStart start)
        {
            start.Error.ShouldBeNull();
            return (await Import(i => i.GetReviewAsync(start.JobIds.ShouldHaveSingleItem(), Ct))).ShouldNotBeNull();
        }

        public void SiteReads(SiteRecipeReading reading) =>
            Scraper.ScrapeAsync(Arg.Any<string>(), Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(WorkerOutcome.Success(reading, "recipe-scrapers 15.14.0"));

        public async ValueTask DisposeAsync()
        {
            await tdb.DisposeAsync();
            await Web.DisposeAsync();
        }
    }

    private static SiteRecipeReading Complete(bool supported = true) =>
        new(supported, true, "Lentil Soup (site)", ["1 cup lentils", "4 cups stock"], ["Rinse.", "Simmer 30 minutes."],
            "4 servings", 40, null, null, "Pat Cook", null, null, "All Recipes Example");

    [Fact]
    [Trait("Req", "AC-110")]
    [Trait("Req", "AC-112")]
    [Trait("Req", "BR-10")]
    public async Task A_supported_page_without_recipe_data_is_read_by_the_site_reader_from_the_fetched_html()
    {
        await using var s = await Scope.CreateAsync(siteReadersOn: true);
        s.Web.Html(SupportedUrl, NoSchemaPage);
        s.SiteReads(Complete());

        var review = await s.ReviewAsync(await s.Import(i => i.StartUrlAsync(SupportedUrl, false, UserId, Ct)));

        review.Recipe.Method.ShouldBe(ExtractionMethod.SiteReader);
        review.Recipe.Title.ShouldBe("Lentil Soup (site)");
        review.Recipe.Ingredients.ShouldBe("1 cup lentils\n4 cups stock");
        review.Recipe.SourceUrl.ShouldBe(SupportedUrl);
        review.Recipe.SourceAuthor.ShouldBe("Pat Cook");
        await s.Scraper.Received(1).ScrapeAsync(Arg.Is<string>(h => h.Contains("Comments are closed.", StringComparison.Ordinal)),
            Arg.Is<Uri>(u => u.AbsoluteUri == SupportedUrl), Arg.Any<CancellationToken>());
        s.Web.Requests.Count.ShouldBe(1, "AC-112: only the page itself was fetched, by SafeFetcher");
    }

    [Fact]
    [Trait("Req", "AC-111")]
    public async Task Complete_recipe_data_wins_and_the_site_reader_is_not_asked()
    {
        await using var s = await Scope.CreateAsync(siteReadersOn: true);
        s.Web.Html(SupportedUrl, CompletePage);
        s.SiteReads(Complete());

        var review = await s.ReviewAsync(await s.Import(i => i.StartUrlAsync(SupportedUrl, false, UserId, Ct)));

        review.Recipe.Method.ShouldBe(ExtractionMethod.JsonLd);
        review.Recipe.Title.ShouldBe("Lentil Soup");
        await s.Scraper.DidNotReceiveWithAnyArgs().ScrapeAsync(default!, default!, Ct);
    }

    [Fact]
    [Trait("Req", "AC-131")]
    public async Task With_site_readers_off_by_default_imports_read_as_before()
    {
        await using var s = await Scope.CreateAsync(siteReadersOn: false);
        (await s.WithAsync(sp => sp.GetRequiredService<SiteReaderSettings>().EnabledAsync(Ct))).ShouldBeFalse();
        s.Web.Html(SupportedUrl, NoSchemaPage);
        s.SiteReads(Complete());

        var review = await s.ReviewAsync(await s.Import(i => i.StartUrlAsync(SupportedUrl, false, UserId, Ct)));

        review.Recipe.Method.ShouldNotBe(ExtractionMethod.SiteReader);
        review.Recipe.Title.ShouldBe("Lentil Soup");
        await s.Scraper.DidNotReceiveWithAnyArgs().ScrapeAsync(default!, default!, Ct);
    }

    [Fact]
    [Trait("Req", "AC-131")]
    [Trait("Req", "BR-119")]
    public async Task A_worker_failure_or_an_unsupported_host_keeps_the_dotnet_reading()
    {
        await using var s = await Scope.CreateAsync(siteReadersOn: true);
        s.Web.Html(SupportedUrl, NoSchemaPage);
        s.Scraper.ScrapeAsync(Arg.Any<string>(), Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(WorkerOutcome.Failed<SiteRecipeReading>(WorkerFailure.Timeout, "took too long"));

        var failed = await s.ReviewAsync(await s.Import(i => i.StartUrlAsync(SupportedUrl, false, UserId, Ct)));
        failed.Recipe.Method.ShouldNotBe(ExtractionMethod.SiteReader);
        failed.Recipe.Title.ShouldBe("Lentil Soup");

        s.SiteReads(Complete(supported: false));
        var unsupported = await s.ReviewAsync(await s.Import(i => i.StartUrlAsync(SupportedUrl, false, UserId, Ct)));
        unsupported.Recipe.Method.ShouldNotBe(ExtractionMethod.SiteReader);
    }

    [Fact]
    [Trait("Req", "AC-110")]
    [Trait("Req", "AC-112")]
    public async Task Pasted_html_with_a_page_address_is_read_by_the_site_reader_without_fetching()
    {
        await using var s = await Scope.CreateAsync(siteReadersOn: true);
        s.SiteReads(Complete());

        var review = await s.ReviewAsync(await s.Import(i => i.StartPasteAsync(NoSchemaPage, SupportedUrl, false, UserId, Ct)));

        review.Recipe.Method.ShouldBe(ExtractionMethod.SiteReader);
        review.Recipe.SourceUrl.ShouldBe(SupportedUrl);
        s.Web.Requests.ShouldBeEmpty();
        s.Web.Lookups.ShouldBeEmpty();

        var plainText = await s.Import(i => i.StartPasteAsync("Lentil Soup\n\nIngredients\n1 cup lentils\n\nSteps\nSimmer.", SupportedUrl, false, UserId, Ct));
        plainText.Error.ShouldBeNull();
        await s.Scraper.Received(1).ScrapeAsync(Arg.Any<string>(), Arg.Any<Uri>(), Arg.Any<CancellationToken>()); // plain text is not HTML
    }
}
