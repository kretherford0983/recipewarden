using System.Net;
using RecipeWarden.Core.Security;

namespace RecipeWarden.Web.Tests;

/// <summary>FR-IMP-12, AC-110, AC-131, ADR 0059: the site-reader switch on /admin/health and the method on the review page.</summary>
[Trait("Req", "FR-IMP-12")]
public sealed class FR_IMP_12_SiteReaderPageTests : IClassFixture<FR_IMP_12_SiteReaderPageTests.Factory>
{
    private const string PageUrl = "http://www.allrecipes.example/recipe/7/lentil-soup";
    private readonly RecipeWardenFactory factory;

    public FR_IMP_12_SiteReaderPageTests(Factory f) => factory = f.Host;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed class Factory : IDisposable
    {
        public Factory()
        {
            Host = new RecipeWardenFactory();
            PythonWebTestKit.UseFakeWorker(Host, """
                if command == "scrape-html":
                    answer({"supported_host": True, "found": True, "title": "Site Lentil Soup", "ingredients": ["1 cup lentils"],
                            "instructions": ["Simmer."], "site_name": "All Recipes Example"})
                else:
                    answer({"lines": [None for _ in req["lines"]]})
                """);
            Host.Web.Html(PageUrl, """
                <html><head><title>Lentil Soup</title></head><body><h1>Lentil Soup</h1>
                <h2>Ingredients</h2><ul><li>1 cup lentils</li></ul></body></html>
                """);
        }

        public RecipeWardenFactory Host { get; }

        public void Dispose() => Host.Dispose();
    }

    [Fact]
    [Trait("Req", "AC-110")]
    [Trait("Req", "AC-131")]
    public async Task Site_readers_start_off_and_once_on_the_review_page_names_the_site_reader()
    {
        using var admin = await factory.SignInAsync("imp12-admin@example.com", AppRoles.Admin);
        var health = WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/health", Ct));
        health.ShouldContain("Site readers for web imports: off");
        health.ShouldContain("Turn site readers on");

        var off = await admin.PostFormAsync("/manage/import?handler=Url", [new("Url", PageUrl)], tokenPage: "/manage/import");
        off.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var offReview = WebUtility.HtmlDecode(await admin.GetStringAsync(off.Headers.Location!.ToString(), Ct));
        offReview.ShouldContain("Read from: Page headings");

        var turned = await admin.PostFormAsync("/admin/health?handler=SiteReaderSwitch", [new("enabled", "true")], tokenPage: "/admin/health");
        turned.Headers.Location!.OriginalString.ShouldStartWith("/admin/health?done=Site%20readers%20turned%20on.");
        WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/health", Ct)).ShouldContain("Site readers for web imports: on");

        var on = await admin.PostFormAsync("/manage/import?handler=Url", [new("Url", PageUrl)], tokenPage: "/manage/import");
        on.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var review = WebUtility.HtmlDecode(await admin.GetStringAsync(on.Headers.Location!.ToString(), Ct));
        review.ShouldContain("Read from: Site reader (recipe-scrapers)");
        review.ShouldContain("Site Lentil Soup");

        (await admin.PostFormAsync("/admin/health?handler=SiteReaderSwitch", [new("enabled", "false")], tokenPage: "/admin/health"))
            .Headers.Location!.OriginalString.ShouldContain("turned%20off");
    }
}
