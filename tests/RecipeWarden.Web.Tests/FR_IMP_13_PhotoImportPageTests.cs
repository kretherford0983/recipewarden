using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using RecipeWarden.Core.AiParser;
using RecipeWarden.Core.Security;

namespace RecipeWarden.Web.Tests;

/// <summary>FR-IMP-13, W11, AC-126, AC-128, AC-129, ADR 0058: the photo form, review photos, card images and /admin/ai.</summary>
[Trait("Req", "FR-IMP-13")]
[Trait("Req", "W11")]
public sealed partial class FR_IMP_13_PhotoImportPageTests : IClassFixture<FR_IMP_13_PhotoImportPageTests.Factory>
{
    private const string ClaudeUrl = "http://api.anthropic.test/v1/messages";
    private readonly RecipeWardenFactory factory;

    public FR_IMP_13_PhotoImportPageTests(Factory f) => factory = f.Host;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed class Factory : IDisposable
    {
        public Factory()
        {
            Host = new RecipeWardenFactory();
            Host.Settings["Ai:ClaudeEndpoint"] = ClaudeUrl;
        }

        public RecipeWardenFactory Host { get; }

        public void Dispose() => Host.Dispose();
    }

    private const string Card = """
        ---
        format: recipe-md/1
        title: "Nana's Lemon Bars"
        ---

        ## Ingredients

        - 2 cups flour
        - [unclear] cup sugar

        ## Steps

        1. Bake the crust.
        """;

    private void Replies(string text) => factory.Web.Serve(ClaudeUrl, 200, "application/json", JsonSerializer.SerializeToUtf8Bytes(new
    {
        content = new[] { new { type = "text", text } },
        usage = new { input_tokens = 2500, output_tokens = 200 },
    }));

    private static (string, string)[] Settings(bool enabled = true, bool readsImages = true) =>
    [
        ("enabled", enabled ? "true" : "false"), ("provider", "Claude"), ("model", "claude-haiku-5-5"), ("apiKey", "fake-test-key-photo-page"),
        ("cap", ""), ("readsImages", readsImages ? "true" : "false"),
    ];

    [GeneratedRegex("<input[^>]*name=\"KeepPhotos\"[^>]*>")]
    private static partial Regex KeepBox();

    [GeneratedRegex("/media/([0-9a-f]{32})")]
    private static partial Regex MediaLink();

    [Fact]
    [Trait("Req", "AC-126")]
    public async Task The_photo_form_appears_only_when_the_model_can_read_images()
    {
        using var admin = await factory.SignInAsync("photo-admin1@example.com", AppRoles.Admin);
        (await admin.PostFormAsync("/admin/ai", Settings(readsImages: false))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var page = await admin.GetStringAsync("/manage/import", Ct);
        page.ShouldNotContain("From photos");
        var refused = await admin.PostFilesAsync("/manage/import?handler=Photos", [("photos", "card.jpg", WebTestHelpers.SampleJpeg(400, 300))]);
        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        WebUtility.HtmlDecode(await refused.TextAsync()).ShouldContain(PhotoImportRules.NoImages);

        (await admin.PostFormAsync("/admin/ai", Settings())).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await admin.GetStringAsync("/manage/import", Ct)).ShouldContain("From photos");
        (await admin.GetStringAsync("/admin/ai", Ct)).ShouldContain("This model can read images");
    }

    [Fact]
    [Trait("Req", "AC-128")]
    [Trait("Req", "AC-129")]
    [Trait("Req", "BR-129")]
    public async Task A_card_is_read_reviewed_with_its_photo_and_saved_with_the_card_image()
    {
        using var admin = await factory.SignInAsync("photo-admin2@example.com", AppRoles.Admin);
        (await admin.PostFormAsync("/admin/ai", Settings())).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        Replies(Card);

        var start = await admin.PostFilesAsync("/manage/import?handler=Photos", [("photos", "card.jpg", WebTestHelpers.SampleJpeg(900, 600))],
            ("SourceName", "Nana's recipe box"), ("KeepPhotos", "true"));
        start.StatusCode.ShouldBe(HttpStatusCode.Redirect, await start.TextAsync());
        var review = start.Headers.Location!.ToString();

        var html = await admin.GetStringAsync(review, Ct);
        html.ShouldContain("filled by the AI parser");
        html.ShouldContain(review + "?handler=Photo&amp;n=0");
        html.ShouldContain("Not readable in the photo");
        KeepBox().Match(html).Value.ShouldContain("checked");
        var photo = await admin.GetAsync(review + "?handler=Photo&n=0", Ct);
        photo.StatusCode.ShouldBe(HttpStatusCode.OK);
        photo.Content.Headers.ContentType!.MediaType.ShouldBe("image/jpeg");
        (await admin.GetAsync(review + "?handler=Photo&n=1", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using (var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false }))
        {
            (await anonymous.GetAsync(review + "?handler=Photo&n=0", Ct)).StatusCode.ShouldNotBe(HttpStatusCode.OK);
        }

        (string, string)[] Form(string ingredients) =>
        [
            ("Input.Title", "Nana's Lemon Bars"), ("Input.Servings", "16"), ("Input.Ingredients", ingredients), ("Input.Steps", "Bake the crust."),
            ("Input.SourceType", "Personal"), ("Input.SourceName", "Nana's recipe box"), ("KeepPhotos", "true"), ("Action", "publish"),
        ];

        var blocked = await admin.PostFormAsync(review, Form("2 cups flour\n[unclear] cup sugar"));
        blocked.StatusCode.ShouldNotBe(HttpStatusCode.Redirect);
        WebUtility.HtmlDecode(await blocked.TextAsync()).ShouldContain("[unclear] cup sugar");

        var saved = await admin.PostFormAsync(review, Form("2 cups flour\n1 cup sugar"));
        saved.StatusCode.ShouldBe(HttpStatusCode.Redirect, await saved.TextAsync());
        var recipe = await admin.GetStringAsync(saved.Headers.Location!.ToString(), Ct);
        recipe.ShouldContain("Recipe card");
        recipe.ShouldContain("Nana&#x27;s recipe box");
        var id = MediaLink().Matches(recipe).Select(m => m.Groups[1].Value).Distinct().ShouldHaveSingleItem();
        using var reader = factory.CreateClient();
        (await reader.GetAsync("/media/" + id, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK, "a published recipe's card image is public");
    }

    [Fact]
    public async Task Admin_can_test_image_reading()
    {
        using var admin = await factory.SignInAsync("photo-admin3@example.com", AppRoles.Admin);
        (await admin.PostFormAsync("/admin/ai", Settings())).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        Replies("Toast");
        var ok = await admin.PostFormAsync("/admin/ai?handler=TestImages");
        ok.StatusCode.ShouldBe(HttpStatusCode.OK, await ok.TextAsync());
        (await ok.TextAsync()).ShouldContain(PhotoImportRules.ImageTestOk);
    }
}
