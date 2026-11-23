using System.Net;
using System.Text;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Import;

namespace RecipeWarden.Web.Tests;

/// <summary>W3, W4, FR-IMP-01 to 07, AC-10, AC-11, AC-15, AC-16, ADR 0024/0025: the import and review pages.</summary>
[Trait("Req", "FR-IMP-06")]
[Trait("Req", "W3")]
public sealed class FR_IMP_01_ImportPageTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Page(string title, string image = "http://cdn.example/dish.jpg") => $$"""
        <html><head><meta property="og:site_name" content="Example Kitchen">
        <script type="application/ld+json">{"@type":"Recipe","name":"{{title}}","recipeYield":"4","image":"{{image}}",
          "description":"&lt;img src=x onerror=alert(1)&gt; tasty",
          "recipeIngredient":["1 cup red lentils","1 lemon, juiced"],"recipeInstructions":["Simmer.","Add lemon."]}</script>
        </head><body></body></html>
        """;

    private async Task<(HttpClient Editor, string ReviewUrl)> ImportAsync(string user, string url, string title, bool copyImage = false)
    {
        factory.Web.Html(url, Page(title));
        factory.Web.Serve("http://cdn.example/dish.jpg", 200, "image/jpeg", WebTestHelpers.SampleJpeg(800, 600));
        var editor = await factory.SignInAsync(user, AppRoles.Editor);
        var fields = new List<(string, string)> { ("Url", url) };
        if (copyImage)
        {
            fields.Add(("CopyImage", "true"));
        }

        var response = await editor.PostFormAsync("/manage/import?handler=Url", fields.ToArray());
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect, await response.TextAsync());
        var location = response.Headers.Location!.ToString();
        location.ShouldMatch(@"^/manage/import/\d+/review$");
        return (editor, location);
    }

    private static (string, string)[] Save(string title, string action = "publish", params (string, string)[] more) =>
    [
        ("Input.Title", title), ("Input.Servings", "4"), ("Input.Ingredients", "1 cup red lentils\n1 lemon, juiced"),
        ("Input.Steps", "Simmer.\nAdd lemon."), ("Input.SourceType", "Manual"), ("Input.SourceUrl", "https://evil.example/"),
        ("Action", action), .. more,
    ];

    [Fact]
    [Trait("Req", "AC-10")]
    [Trait("Req", "AC-11")]
    [Trait("Req", "BR-10")]
    public async Task Import_review_and_publish_with_the_image()
    {
        var (editor, review) = await ImportAsync("imp-1@example.com", "http://kitchen.example/lentil", "Lentil Lemon Soup", copyImage: true);
        using var _ = editor;

        var page = WebUtility.HtmlDecode(await editor.GetStringAsync(review, Ct));
        page.ShouldContain("value=\"Lentil Lemon Soup\"");
        page.ShouldContain("1 cup red lentils\n1 lemon, juiced");
        page.ShouldContain("The credit for a web recipe is kept as imported");
        page.ShouldContain("Copy the finished-dish image from the source");
        page.ShouldNotContain("Possible duplicate");

        var saved = await editor.PostFormAsync(review, Save("Lentil Lemon Soup", more: ("CopyImage", "true")));
        saved.StatusCode.ShouldBe(HttpStatusCode.Redirect, await saved.TextAsync());
        var slug = saved.Headers.Location!.ToString();
        slug.ShouldBe("/recipes/lentil-lemon-soup");

        using var reader = factory.CreateHttpsClient();
        var html = await reader.GetStringAsync(slug, Ct);
        html.ShouldContain("Example Kitchen");
        html.ShouldContain("http://kitchen.example/lentil");
        html.ShouldNotContain("evil.example");
        var src = System.Text.RegularExpressions.Regex.Match(html, "class=\"finish-image\" src=\"(/media/[0-9a-f]{32})\"").Groups[1].Value;
        src.ShouldNotBeEmpty();
        var image = await reader.GetAsync(src, Ct);
        image.StatusCode.ShouldBe(HttpStatusCode.OK);
        image.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");
        image.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
        (await reader.GetAsync(src + "/thumb", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync(src + "/huge", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await editor.GetAsync(review, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await editor.GetStringAsync(review, Ct)).ShouldContain(Pages.Manage.Import.ReviewModel.AlreadyDone);
        (await editor.PostFormAsync(review, Save("Again"))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    [Trait("Req", "AC-11")]
    public async Task Without_the_tick_no_image_is_fetched()
    {
        var (editor, review) = await ImportAsync("imp-2@example.com", "http://kitchen.example/no-image", "No Image Soup");
        using var _ = editor;
        var before = factory.Web.Requests.Count(r => r.Host == "cdn.example");
        (await editor.PostFormAsync(review, Save("No Image Soup"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        factory.Web.Requests.Count(r => r.Host == "cdn.example").ShouldBe(before);
        (await editor.GetStringAsync("/recipes/no-image-soup", Ct)).ShouldNotContain("finish-image");
    }

    [Fact]
    [Trait("Req", "AC-16")]
    public async Task A_second_import_of_a_page_needs_a_choice()
    {
        var (editor, first) = await ImportAsync("imp-3@example.com", "http://kitchen.example/dup", "Dup Soup");
        using var _ = editor;
        (await editor.PostFormAsync(first, Save("Dup Soup"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        factory.Web.Html("http://kitchen.example/dup?utm_source=x", Page("Dup Soup"));
        var again = await editor.PostFormAsync("/manage/import?handler=Url", ("Url", "http://kitchen.example/dup?utm_source=x"));
        var review = again.Headers.Location!.ToString();
        var html = WebUtility.HtmlDecode(await editor.GetStringAsync(review, Ct));
        html.ShouldContain("Possible duplicate:");
        html.ShouldContain("href=\"/recipes/dup-soup\"");

        var refused = await editor.PostFormAsync(review, Save("Dup Soup"));
        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        WebUtility.HtmlDecode(await refused.TextAsync()).ShouldContain(ImportService.DuplicateChoiceNeeded);

        var kept = await editor.PostFormAsync(review, Save("Dup Soup", more: ("SaveAnyway", "true")));
        kept.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        kept.Headers.Location!.ToString().ShouldBe("/recipes/dup-soup-2");
    }

    [Fact]
    [Trait("Req", "ADR-0024")]
    public async Task A_blocked_site_opens_the_paste_form_and_paste_works()
    {
        using var editor = await factory.SignInAsync("imp-4@example.com", AppRoles.Editor);
        factory.Web.Serve("http://blocked.example/r", 403, "text/html", "bot check"u8.ToArray());

        var refused = await editor.PostFormAsync("/manage/import?handler=Url", ("Url", "http://blocked.example/r"));
        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var html = WebUtility.HtmlDecode(await refused.TextAsync());
        html.ShouldContain(SafeFetcher.SiteRefusedMessage);
        html.ShouldContain("<details class=\"import-box\" open=\"open\" id=\"paste\">");
        html.ShouldContain("id=\"PasteUrl\" name=\"PasteUrl\" type=\"url\" value=\"http://blocked.example/r\"");

        var pasted = await editor.PostFormAsync("/manage/import?handler=Paste", ("PasteUrl", "http://blocked.example/r"),
            ("PasteText", "Blocked Bread\n\nIngredients\n2 cups flour\n\nDirections\nBake."));
        pasted.StatusCode.ShouldBe(HttpStatusCode.Redirect, await pasted.TextAsync());
        WebUtility.HtmlDecode(await editor.GetStringAsync(pasted.Headers.Location!.ToString(), Ct)).ShouldContain("value=\"Blocked Bread\"");
    }

    [Fact]
    [Trait("Req", "AC-13")]
    public async Task A_home_network_address_is_refused_without_connecting()
    {
        using var editor = await factory.SignInAsync("imp-5@example.com", AppRoles.Editor);
        var before = factory.Web.Connections.Count;
        var refused = await editor.PostFormAsync("/manage/import?handler=Url", ("Url", "http://192.168.1.10/recipes"));
        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        WebUtility.HtmlDecode(await refused.TextAsync()).ShouldContain(UrlRules.PrivateAddress);
        factory.Web.Connections.Count.ShouldBe(before);
    }

    [Fact]
    [Trait("Req", "W4")]
    [Trait("Req", "AC-15")]
    public async Task Files_are_read_and_bad_files_are_reported()
    {
        using var editor = await factory.SignInAsync("imp-6@example.com", AppRoles.Editor);
        var md = Encoding.UTF8.GetBytes("# Tea Time\n## Ingredients\n- 1 tea bag\n## Steps\n1. Steep.\n\n# Coffee Time\n## Ingredients\n- 2 Tbsp coffee\n## Steps\n1. Brew.");

        var ok = await editor.PostFilesAsync("/manage/import?handler=Files", [("files", "drinks.md", md)]);
        ok.StatusCode.ShouldBe(HttpStatusCode.Redirect, await ok.TextAsync());
        var review = WebUtility.HtmlDecode(await editor.GetStringAsync(ok.Headers.Location!.ToString(), Ct));
        review.ShouldContain("This import found 2 recipes:");
        review.ShouldContain("Coffee Time");

        var mixed = await editor.PostFilesAsync("/manage/import?handler=Files", [("files", "drinks2.md", md), ("files", "macro.docm", [0x50, 0x4B, 3, 4])]);
        mixed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var mixedHtml = WebUtility.HtmlDecode(await mixed.TextAsync());
        mixedHtml.ShouldContain(UploadRules.Macro);
        mixedHtml.ShouldContain("2 recipes are ready to review below.");

        var big = new byte[60 * 1024 * 1024];
        "%PDF-1.7"u8.CopyTo(big);
        var tooBig = await editor.PostFilesAsync("/manage/import?handler=Files", [("files", "huge.pdf", big)]);
        tooBig.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        WebUtility.HtmlDecode(await tooBig.TextAsync()).ShouldContain(UploadRules.DocumentTooBig);

        var exe = await editor.PostFilesAsync("/manage/import?handler=Files", [("files", "recipe.pdf", "MZ\u0090\0"u8.ToArray())]);
        WebUtility.HtmlDecode(await exe.TextAsync()).ShouldContain(UploadRules.Mismatch);
    }

    [Fact]
    [Trait("Req", "AC-76")]
    [Trait("Req", "BR-13")]
    public async Task Markup_from_a_page_is_shown_as_text()
    {
        var (editor, review) = await ImportAsync("imp-7@example.com", "http://kitchen.example/xss", "Safe Soup");
        using var _ = editor;
        var html = await editor.GetStringAsync(review, Ct);
        html.ShouldNotContain("<img src=x onerror");
        WebUtility.HtmlDecode(html).ShouldContain("<img src=x onerror=alert(1)> tasty");
    }

    [Fact]
    public async Task Discard_removes_it_from_the_list()
    {
        var (editor, review) = await ImportAsync("imp-8@example.com", "http://kitchen.example/discard", "Discard Soup");
        using var _ = editor;
        (await editor.GetStringAsync("/manage/import", Ct)).ShouldContain("Discard Soup");
        var discarded = await editor.PostFormAsync(review + "?handler=Discard");
        discarded.Headers.Location!.ToString().ShouldBe("/manage/import");
        (await editor.GetStringAsync("/manage/import", Ct)).ShouldNotContain("Discard Soup");
        (await editor.GetAsync("/manage/import/99999/review", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Req", "SEC-30")]
    public async Task Import_posts_need_a_token()
    {
        using var editor = await factory.SignInAsync("imp-9@example.com", AppRoles.Editor);
        var response = await editor.PostAsync("/manage/import?handler=Url",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["Url"] = "http://kitchen.example/x" }), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}

/// <summary>SEC-43: URL imports are limited per user per hour; opening the page is not counted.</summary>
[Trait("Req", "SEC-43")]
public sealed class SEC_43_UrlImportLimitTests : IAsyncDisposable
{
    private readonly RecipeWardenFactory factory = new();

    public SEC_43_UrlImportLimitTests() => factory.Settings["RateLimits:UrlImportsPerHour"] = "2";

    [Fact]
    public async Task The_third_url_import_in_an_hour_gets_429()
    {
        using var editor = await factory.SignInAsync("limit-import@example.com", AppRoles.Editor);
        for (var i = 0; i < 3; i++)
        {
            (await editor.GetAsync("/manage/import", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await editor.PostFormAsync("/manage/import?handler=Url", ("Url", "http://nowhere.example/1"))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await editor.PostFormAsync("/manage/import?handler=Url", ("Url", "http://nowhere.example/2"))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var third = await editor.PostFormAsync("/manage/import?handler=Url", ("Url", "http://nowhere.example/3"));
        third.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await editor.PostFormAsync("/manage/import?handler=Paste", ("PasteText", "x"))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();
}
