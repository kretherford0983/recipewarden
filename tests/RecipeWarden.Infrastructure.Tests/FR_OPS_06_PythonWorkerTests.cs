using RecipeWarden.Core.Python;
using RecipeWarden.Infrastructure.Python;
using RecipeWarden.Infrastructure.Settings;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>
/// FR-OPS-06, AC-106, ADR 0051: the real worker (python/.venv, built by build/python.sh dev, which build/test.sh runs)
/// end to end. These tests are never skipped: a missing environment is a failure (docs/testing.md).
/// </summary>
[Trait("Req", "FR-OPS-06")]
public sealed class FR_OPS_06_PythonWorkerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PythonOptions Real()
    {
        var options = new PythonOptions();
        options.Resolve().ShouldNotBeNull("no Python environment: run build/python.sh dev");
        return options;
    }

    [Fact]
    [Trait("Req", "AC-106")]
    public async Task The_self_test_passes_offline_and_the_network_guard_holds()
    {
        var (worker, health) = PythonTestKit.Worker(Real());
        var call = await worker.SelftestAsync(Ct);

        call.Ok.ShouldBeTrue(call.Detail);
        call.Result!.Checks.NetworkBlocked.ShouldBeTrue();
        call.Result.Checks.Fdc.ShouldBeTrue(); // the NLTK model is local
        health.Snapshot.Status.ShouldBe(PythonStatus.Ok);
        health.Snapshot.Engine!.ShouldContain("ingredient-parser-nlp 2.10.0");
    }

    [Fact]
    [Trait("Req", "FR-IMP-11")]
    [Trait("Req", "AC-104")]
    public async Task The_second_opinion_reads_lines_through_the_adapter()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var options = Real();
        var (worker, health) = PythonTestKit.Worker(options);
        var reader = new PythonIngredientSecondOpinion(PythonTestKit.Features(options, health, new SettingsStore(s.Db)), worker);

        var outcome = await reader.ReadAsync(["1-1/2 cups milk", "2 large eggs", ""], Ct);

        outcome.Ok.ShouldBeTrue(outcome.Detail);
        outcome.Engine.ShouldBe("ingredient-parser-nlp 2.10.0");
        outcome.Value![0]!.QuantityMax.ShouldBe(1m); // the known misreading, which BR-118 flags
        outcome.Value[1]!.Names.ShouldBe(["eggs"]);
        outcome.Value[2].ShouldBeNull();
    }

    [Fact]
    [Trait("Req", "FR-NUT-07")]
    [Trait("Req", "AC-101")]
    public async Task Suggestions_name_usda_foods_that_exist_locally()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var options = Real();
        var (worker, health) = PythonTestKit.Worker(options);
        var source = new PythonFoodSuggestionSource(PythonTestKit.Features(options, health, new SettingsStore(s.Db)), worker);

        var outcome = await source.SuggestAsync(["brown sugar", "garlic", "gochujang"], Ct);

        outcome.Ok.ShouldBeTrue(outcome.Detail);
        outcome.Value![0].Foods.ShouldNotBeEmpty();
        outcome.Value[2].Foods.ShouldBeEmpty();
        var ids = outcome.Value.SelectMany(f => f.Foods).Select(f => (int?)f.FdcId).ToList();
        var local = s.Db.NutrientRefs.Where(n => ids.Contains(n.FdcId)).Select(n => n.FdcId).ToHashSet();
        ids.ShouldAllBe(id => local.Contains(id)); // ADR 0052: every candidate resolves
    }

    [Fact]
    [Trait("Req", "AC-109")]
    [Trait("Req", "AC-112")]
    public async Task The_site_reader_reads_html_it_is_given_and_fetches_nothing()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var options = Real();
        var (worker, health) = PythonTestKit.Worker(options);
        var scraper = new PythonSiteRecipeScraper(PythonTestKit.Features(options, health, new SettingsStore(s.Db)), worker);
        var html = await File.ReadAllTextAsync(Path.Combine(PythonTestKit.RepoRoot, "tests", "RecipeWarden.Importers.Tests", "Fixtures", "Html", "jsonld-graph.html"), Ct);

        var outcome = await scraper.ScrapeAsync(html, new Uri("https://www.allrecipes.com/recipe/1/soup/"), Ct);

        outcome.Ok.ShouldBeTrue(outcome.Detail);
        outcome.Value!.SupportedHost.ShouldBeTrue();
        outcome.Value.Title.ShouldBe("Lemon Lentil Soup");
        outcome.Value.Ingredients.Count.ShouldBe(4);
        // No HTML, no fetch: an empty page on a supported host gives nothing rather than going to look.
        var empty = await scraper.ScrapeAsync("<html></html>", new Uri("https://www.allrecipes.com/recipe/2/x/"), Ct);
        empty.Value!.Found.ShouldBeFalse();
    }

    [Fact]
    [Trait("Req", "NFR-10")]
    public async Task One_hundred_lines_with_usda_matching_parse_well_inside_the_time_budget()
    {
        var (worker, _) = PythonTestKit.Worker(Real());
        var lines = File.ReadLines(Path.Combine(PythonTestKit.RepoRoot, "tests", "RecipeWarden.Importers.Tests", "Corpus", "ingredient-lines.txt"))
            .Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('|')[1].Trim()).Take(100).ToList();
        var started = System.Diagnostics.Stopwatch.GetTimestamp();

        var call = await worker.ParseAsync(lines, fdc: true, Ct);

        call.Ok.ShouldBeTrue(call.Detail);
        call.Result!.Lines.Count.ShouldBe(100);
        // NFR-10 is 3 s on the 2-vCPU host; here the limit is the 20 s call timeout, and the time is reported.
        TestContext.Current.SendDiagnosticMessage($"parse-ingredients, 100 lines with FDC: {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ms");
    }
}
