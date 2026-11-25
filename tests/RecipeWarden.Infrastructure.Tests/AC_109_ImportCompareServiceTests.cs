using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RecipeWarden.Core.Import;
using RecipeWarden.Core.Python;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Import;
using RecipeWarden.Infrastructure.Python;
using RecipeWarden.Infrastructure.Settings;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>AC-109, AC-112, D6: import-compare re-reads saved URL imports, both ways, and changes nothing.</summary>
[Trait("Req", "AC-109")]
public sealed class AC_109_ImportCompareServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string JsonLdPage = File.ReadAllText(Path.Combine(PythonTestKit.RepoRoot, "tests", "RecipeWarden.Importers.Tests", "Fixtures", "Html", "jsonld-graph.html"));

    private static async Task SaveImportAsync(RecipeTestScope s, string batch, string url, string html, string? rawPath = null)
    {
        Directory.CreateDirectory(Path.Combine(s.Data.ImportsDirectory, batch));
        await File.WriteAllTextAsync(Path.Combine(s.Data.ImportsDirectory, batch, "0.raw"), html, Ct);
        s.Db.ImportJobs.Add(new ImportJob
        {
            BatchId = batch, SourceKind = ImportSourceKind.Url, SourceRef = url, Status = ImportStatus.Review, RawPath = rawPath ?? batch + "/0.raw",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await s.Db.SaveChangesAsync(Ct);
    }

    private static SiteRecipeReading Reading(bool supported, bool complete) =>
        new(supported, true, complete ? "Soup" : null, ["1 cup lentils"], complete ? ["Simmer."] : [], null, null, null, null, null, null, null, null);

    [Fact]
    public async Task Each_saved_page_is_read_both_ways_and_nothing_is_written()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await SaveImportAsync(s, "b1", "https://blog.example/soup", JsonLdPage);
        await SaveImportAsync(s, "b2", "https://www.allrecipes.com/recipe/1/x/", "<html><body><p>no schema data here</p></body></html>");
        await SaveImportAsync(s, "b3", "https://gone.example/r", "x");
        File.Delete(Path.Combine(s.Data.ImportsDirectory, "b3", "0.raw")); // purged after 30 days
        await SaveImportAsync(s, "b4", "https://evil.example/r", "x", rawPath: "../db/recipewarden.db"); // never read outside imports
        var scraper = Substitute.For<ISiteRecipeScraper>();
        scraper.ScrapeAsync(Arg.Any<string>(), Arg.Is<Uri>(u => u.Host == "blog.example"), Arg.Any<CancellationToken>())
            .Returns(WorkerOutcome.Success(Reading(false, true), "recipe-scrapers 15.14.0"));
        scraper.ScrapeAsync(Arg.Any<string>(), Arg.Is<Uri>(u => u.Host == "www.allrecipes.com"), Arg.Any<CancellationToken>())
            .Returns(WorkerOutcome.Success(Reading(true, true), "recipe-scrapers 15.14.0"));
        var before = Snapshot(s);
        var jobs = await s.Db.ImportJobs.AsNoTracking().Select(j => new { j.Id, j.UpdatedAt, j.Status }).ToListAsync(Ct);

        var report = await new ImportCompareService(s.Db, s.Data, scraper).RunAsync(Ct);

        report.Failure.ShouldBe(WorkerFailure.None);
        report.Missing.ShouldBe(2);
        report.Rows.Select(r => r.Verdict).ShouldBe([CompareVerdict.BothComplete, CompareVerdict.OnlySiteReader]);
        report.Rows[0].DotNetMethod.ShouldBe(ExtractionMethod.JsonLd);
        await scraper.Received(2).ScrapeAsync(Arg.Any<string>(), Arg.Any<Uri>(), Arg.Any<CancellationToken>());
        Snapshot(s).ShouldBe(before);
        (await s.Db.ImportJobs.AsNoTracking().Select(j => new { j.Id, j.UpdatedAt, j.Status }).ToListAsync(Ct)).ShouldBe(jobs);
    }

    [Fact]
    [Trait("Req", "BR-119")]
    public async Task Without_a_site_reader_it_stops_and_says_why()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await SaveImportAsync(s, "b1", "https://blog.example/soup", JsonLdPage);
        var scraper = Substitute.For<ISiteRecipeScraper>();
        scraper.ScrapeAsync(Arg.Any<string>(), Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(WorkerOutcome.Failed<SiteRecipeReading>(WorkerFailure.Unavailable, "no Python runtime was found"));

        var report = await new ImportCompareService(s.Db, s.Data, scraper).RunAsync(Ct);

        report.Failure.ShouldBe(WorkerFailure.Unavailable);
        report.Rows.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Req", "AC-112")]
    public async Task With_the_real_site_reader_a_saved_page_compares_without_any_fetch()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await SaveImportAsync(s, "b1", "https://blog.example/soup", JsonLdPage);
        var options = new PythonOptions();
        var (worker, health) = PythonTestKit.Worker(options);
        var scraper = new PythonSiteRecipeScraper(PythonTestKit.Features(options, health, new SettingsStore(s.Db)), worker);

        var report = await new ImportCompareService(s.Db, s.Data, scraper).RunAsync(Ct);

        report.Failure.ShouldBe(WorkerFailure.None, report.Detail);
        var row = report.Rows.ShouldHaveSingleItem();
        row.Verdict.ShouldBe(CompareVerdict.BothComplete);
        (row.DotNetIngredients, row.SiteIngredients).ShouldBe((4, 4));
        health.Snapshot.LastFailure.ShouldBe(WorkerFailure.None);
    }

    private static string Snapshot(RecipeTestScope s) =>
        string.Join('\n', Directory.GetFiles(s.Data.ImportsDirectory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(f => f + " " + new FileInfo(f).Length + " " + File.GetLastWriteTimeUtc(f).Ticks));
}
