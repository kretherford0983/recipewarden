using System.Text;
using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Import;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Import;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Tests.Shared;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>FR-IMP-01 to 08, AC-10, AC-11, AC-16, BR-10, BR-12, W3, W4, ADR 0024/0025: the import service end to end.</summary>
[Trait("Req", "FR-IMP-01")]
public sealed class FR_IMP_01_ImportServiceTests : IAsyncDisposable
{
    private const string PageUrl = "http://kitchen.example/recipes/lemon-soup";
    private const string ImageUrl = "http://cdn.example/soup.jpg";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeWeb web = new();

    private static string Page(string title = "Lemon Soup", string image = ImageUrl) => $$"""
        <html><head><meta property="og:site_name" content="Example Kitchen">
        <script type="application/ld+json">{"@type":"Recipe","name":"{{title}}","recipeYield":"4",
          "image":"{{image}}","author":"Jane Doe",
          "recipeIngredient":["1 cup red lentils","1 lemon, juiced","4 cups broth"],
          "recipeInstructions":["Simmer the lentils.","Add the lemon."]}</script></head><body></body></html>
        """;

    public FR_IMP_01_ImportServiceTests()
    {
        web.Html(PageUrl, Page());
        web.Serve(ImageUrl, 200, "image/jpeg", TestImages.JpegWithExif(1, "GPS-HERE"));
    }

    private static RecipeEdit EditFrom(ImportReview review) => new()
    {
        Title = review.Recipe.Title,
        Servings = 4,
        IngredientsText = review.Recipe.Ingredients,
        StepsText = review.Recipe.Steps,
        SourceType = SourceType.Manual,
        SourceUrl = "https://evil.example/other",
        SourceName = "Someone else",
        WantsPublish = true,
    };

    [Fact]
    [Trait("Req", "AC-10")]
    [Trait("Req", "W3")]
    public async Task A_url_import_creates_a_job_to_review_and_keeps_the_raw_page()
    {
        await using var t = await RecipeTestScope.CreateAsync(web: web);
        var start = await t.Import.StartUrlAsync(PageUrl, copyImage: false, RecipeTestScope.UserId, Ct);

        start.Error.ShouldBeNull();
        var review = (await t.Import.GetReviewAsync(start.JobIds.ShouldHaveSingleItem(), Ct))!;
        review.Kind.ShouldBe(ImportSourceKind.Url);
        review.Status.ShouldBe(ImportStatus.Review);
        review.Recipe.Title.ShouldBe("Lemon Soup");
        review.Recipe.Ingredients.ShouldBe("1 cup red lentils\n1 lemon, juiced\n4 cups broth");
        review.Recipe.SourceName.ShouldBe("Example Kitchen");
        review.ImageUrl.ShouldBe(ImageUrl);
        review.CreditLocked.ShouldBeTrue();
        review.Duplicate.ShouldBeNull();

        var job = await t.Db.ImportJobs.SingleAsync(Ct);
        File.ReadAllText(Path.Combine(t.Data.ImportsDirectory, job.RawPath!)).ShouldContain("Lemon Soup");
        (await t.Import.ListOpenAsync(Ct)).ShouldHaveSingleItem().Title.ShouldBe("Lemon Soup");
    }

    [Fact]
    [Trait("Req", "AC-11")]
    [Trait("Req", "BR-12")]
    public async Task Unticked_image_is_never_downloaded()
    {
        await using var t = await RecipeTestScope.CreateAsync(web: web);
        var start = await t.Import.StartUrlAsync(PageUrl, false, RecipeTestScope.UserId, Ct);
        var review = (await t.Import.GetReviewAsync(start.JobIds[0], Ct))!;

        var saved = await t.Import.SaveAsync(review.JobId, EditFrom(review), copyImage: false, saveAnyway: false, RecipeTestScope.UserId, Ct);

        saved.Outcome!.Status.ShouldBe(SaveStatus.Saved);
        web.Requests.ShouldNotContain(r => r.Host == "cdn.example");
        (await t.Db.Images.CountAsync(Ct)).ShouldBe(0);
        (await t.Db.Recipes.SingleAsync(Ct)).FinishImageId.ShouldBeNull();
    }

    [Fact]
    [Trait("Req", "AC-11")]
    [Trait("Req", "BR-12")]
    [Trait("Req", "SEC-52")]
    public async Task Ticked_image_is_downloaded_re_encoded_and_its_source_stored()
    {
        await using var t = await RecipeTestScope.CreateAsync(web: web);
        var start = await t.Import.StartUrlAsync(PageUrl, true, RecipeTestScope.UserId, Ct);
        var review = (await t.Import.GetReviewAsync(start.JobIds[0], Ct))!;
        review.CopyImage.ShouldBeTrue();

        var saved = await t.Import.SaveAsync(review.JobId, EditFrom(review), true, false, RecipeTestScope.UserId, Ct);

        saved.ImageWarning.ShouldBeNull();
        var recipe = await t.Db.Recipes.SingleAsync(Ct);
        var image = await t.Db.Images.SingleAsync(Ct);
        recipe.FinishImageId.ShouldBe(image.Id);
        image.SourceUrl.ShouldBe(ImageUrl);
        image.ContentType.ShouldBe("image/webp");
        TestImages.Contains(File.ReadAllBytes(Path.Combine(t.Data.MediaDirectory, image.StoragePath)), "GPS-HERE").ShouldBeFalse();
    }

    [Fact]
    public async Task A_failed_image_copy_still_saves_the_recipe()
    {
        await using var t = await RecipeTestScope.CreateAsync(web: web);
        web.Html("http://kitchen.example/private-image", Page("Pic Soup", "http://10.0.0.5/x.jpg"));
        var start = await t.Import.StartUrlAsync("http://kitchen.example/private-image", true, RecipeTestScope.UserId, Ct);
        var review = (await t.Import.GetReviewAsync(start.JobIds[0], Ct))!;
        var saved = await t.Import.SaveAsync(review.JobId, EditFrom(review), true, false, RecipeTestScope.UserId, Ct);
        saved.Outcome!.Status.ShouldBe(SaveStatus.Saved);
        saved.ImageWarning.ShouldBe(ImportService.ImageNotCopied + ": " + UrlRules.PrivateAddress);
    }

    [Fact]
    [Trait("Req", "BR-10")]
    [Trait("Req", "AC-12")]
    public async Task A_web_import_keeps_its_credit_whatever_the_form_says()
    {
        await using var t = await RecipeTestScope.CreateAsync(web: web);
        var start = await t.Import.StartUrlAsync(PageUrl, false, RecipeTestScope.UserId, Ct);
        var review = (await t.Import.GetReviewAsync(start.JobIds[0], Ct))!;
        var saved = await t.Import.SaveAsync(review.JobId, EditFrom(review), false, false, RecipeTestScope.UserId, Ct);

        var recipe = await t.Db.Recipes.SingleAsync(r => r.Id == saved.Outcome!.Id, Ct);
        recipe.SourceType.ShouldBe(SourceType.Web);
        recipe.SourceUrl.ShouldBe(PageUrl);
        recipe.SourceName.ShouldBe("Example Kitchen");
        recipe.SourceAuthor.ShouldBe("Jane Doe");
        recipe.SourceImportedAt.ShouldNotBeNull();
        (await t.Db.ImportJobs.SingleAsync(Ct)).Status.ShouldBe(ImportStatus.Saved);
        (await t.Import.SaveAsync(review.JobId, EditFrom(review), false, false, RecipeTestScope.UserId, Ct)).Outcome!.Status.ShouldBe(SaveStatus.NotFound);
    }

    [Fact]
    [Trait("Req", "AC-16")]
    [Trait("Req", "FR-IMP-07")]
    public async Task The_same_page_again_needs_a_choice_before_saving()
    {
        await using var t = await RecipeTestScope.CreateAsync(web: web);
        var first = await t.Import.StartUrlAsync(PageUrl, false, RecipeTestScope.UserId, Ct);
        var r1 = (await t.Import.GetReviewAsync(first.JobIds[0], Ct))!;
        var existing = await t.Import.SaveAsync(r1.JobId, EditFrom(r1), false, false, RecipeTestScope.UserId, Ct);

        web.Html(PageUrl + "?utm_source=newsletter", Page());
        var again = await t.Import.StartUrlAsync(PageUrl + "?utm_source=newsletter", false, RecipeTestScope.UserId, Ct);
        var r2 = (await t.Import.GetReviewAsync(again.JobIds[0], Ct))!;
        r2.Duplicate.ShouldNotBeNull();
        r2.Duplicate!.Id.ShouldBe(existing.Outcome!.Id);

        var blocked = await t.Import.SaveAsync(r2.JobId, EditFrom(r2), false, saveAnyway: false, RecipeTestScope.UserId, Ct);
        blocked.NeedsDuplicateChoice.ShouldBeTrue();
        blocked.Outcome.ShouldBeNull();
        (await t.Db.Recipes.CountAsync(Ct)).ShouldBe(1);

        var anyway = await t.Import.SaveAsync(r2.JobId, EditFrom(r2), false, saveAnyway: true, RecipeTestScope.UserId, Ct);
        anyway.Outcome!.Status.ShouldBe(SaveStatus.Saved);
        (await t.Db.Recipes.CountAsync(Ct)).ShouldBe(2);
    }

    [Fact]
    [Trait("Req", "AC-16")]
    public async Task Same_title_and_ingredients_is_a_duplicate_from_any_source()
    {
        await using var t = await RecipeTestScope.CreateAsync(web: web);
        await t.AddAsync("Lemon Soup", "1 cup red lentils\n1 lemon, juiced\n4 cups broth");
        var start = await t.Import.StartPasteAsync("Lemon Soup\n\nIngredients\n1 cup red lentils\n1 lemon, juiced\n4 cups broth\n\nSteps\nCook.", null, false, RecipeTestScope.UserId, Ct);
        (await t.Import.GetReviewAsync(start.JobIds[0], Ct))!.Duplicate!.Title.ShouldBe("Lemon Soup");
    }

    [Fact]
    [Trait("Req", "ADR-0024")]
    public async Task A_refusing_site_offers_paste_and_a_private_address_does_not()
    {
        await using var t = await RecipeTestScope.CreateAsync(web: web);
        web.Serve("http://blocked.example/r", 403, "text/html", "bot check"u8.ToArray());
        var refused = await t.Import.StartUrlAsync("http://blocked.example/r", false, RecipeTestScope.UserId, Ct);
        refused.Error.ShouldBe(SafeFetcher.SiteRefusedMessage);
        refused.OfferPaste.ShouldBeTrue();

        web.Html("http://kitchen.example/about", "<html><body><h1>About</h1></body></html>");
        var noRecipe = await t.Import.StartUrlAsync("http://kitchen.example/about", false, RecipeTestScope.UserId, Ct);
        noRecipe.OfferPaste.ShouldBeTrue();

        var internalSite = await t.Import.StartUrlAsync("http://192.168.1.10/", false, RecipeTestScope.UserId, Ct);
        internalSite.Error.ShouldBe(UrlRules.PrivateAddress);
        internalSite.OfferPaste.ShouldBeFalse();
        (await t.Db.ImportJobs.CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    [Trait("Req", "ADR-0024")]
    public async Task Pasted_text_with_an_address_keeps_the_web_credit()
    {
        await using var t = await RecipeTestScope.CreateAsync(web: web);
        var start = await t.Import.StartPasteAsync("Toast\n\nIngredients\n2 slices bread\n\nDirections\nToast it.", "https://www.blocked.example/toast", false, RecipeTestScope.UserId, Ct);
        var review = (await t.Import.GetReviewAsync(start.JobIds[0], Ct))!;
        review.Kind.ShouldBe(ImportSourceKind.Paste);
        review.Recipe.SourceUrl.ShouldBe("https://www.blocked.example/toast");
        review.CreditLocked.ShouldBeTrue();
        web.Requests.ShouldBeEmpty();

        (await t.Import.StartPasteAsync(" ", null, false, RecipeTestScope.UserId, Ct)).Error.ShouldBe(ImportService.PasteEmpty);
        (await t.Import.StartPasteAsync("x", "http://10.0.0.5/", false, RecipeTestScope.UserId, Ct)).Error.ShouldBe(UrlRules.PrivateAddress);
        (await t.Import.StartPasteAsync(new string('a', 6 * 1024 * 1024), null, false, RecipeTestScope.UserId, Ct)).Error.ShouldBe(ImportService.PasteTooLong);
    }

    [Fact]
    [Trait("Req", "W4")]
    [Trait("Req", "FR-IMP-04")]
    public async Task Files_give_one_job_per_recipe_and_report_bad_files()
    {
        await using var t = await RecipeTestScope.CreateAsync(web: web);
        var md = "# Tea\n## Ingredients\n- 1 tea bag\n## Steps\n1. Steep.\n\n# Coffee\n## Ingredients\n- 2 Tbsp coffee\n## Steps\n1. Brew.";
        var files = new List<ImportFile>
        {
            new("drinks.md", new MemoryStream(Encoding.UTF8.GetBytes(md)), md.Length),
            new("virus.exe", new MemoryStream("MZ"u8.ToArray()), 2),
            new("note.txt", new MemoryStream("just a note"u8.ToArray()), 11),
        };

        var start = await t.Import.StartFilesAsync(files, RecipeTestScope.UserId, Ct);

        start.JobIds.Count.ShouldBe(2);
        start.FileProblems.ShouldBe([new FileProblem("virus.exe", UploadRules.NotAllowed), new FileProblem("note.txt", RecipeWarden.Importers.Import.ImportReader.NoRecipeInFile)]);
        var review = (await t.Import.GetReviewAsync(start.JobIds[1], Ct))!;
        review.Recipe.Title.ShouldBe("Coffee");
        review.Kind.ShouldBe(ImportSourceKind.Markdown);
        review.Batch.Select(b => b.Title).ShouldBe(["Tea", "Coffee"]);
        review.Recipe.SourceType.ShouldBe(SourceType.File);

        (await t.Import.StartFilesAsync([], RecipeTestScope.UserId, Ct)).Error.ShouldBe(ImportService.NoFiles);
        var eleven = Enumerable.Range(0, 11).Select(i => new ImportFile($"{i}.md", new MemoryStream(), 0)).ToList();
        (await t.Import.StartFilesAsync(eleven, RecipeTestScope.UserId, Ct)).Error.ShouldBe(ImportService.TooManyFiles);
    }

    [Fact]
    [Trait("Req", "FR-IMP-08")]
    public async Task Jobs_and_raw_payloads_are_purged_after_30_days()
    {
        var clock = new ManualClock();
        await using var t = await RecipeTestScope.CreateAsync(clock, web);
        var start = await t.Import.StartUrlAsync(PageUrl, false, RecipeTestScope.UserId, Ct);
        (await t.Import.DiscardAsync(start.JobIds[0], Ct)).ShouldBeTrue();
        (await t.Import.DiscardAsync(start.JobIds[0], Ct)).ShouldBeFalse();
        (await t.Import.ListOpenAsync(Ct)).ShouldBeEmpty();
        var folder = Path.Combine(t.Data.ImportsDirectory, start.BatchId!);
        Directory.Exists(folder).ShouldBeTrue();

        (await t.Import.PurgeAsync(Ct)).ShouldBe(0);
        clock.Advance(TimeSpan.FromDays(31));
        (await t.Import.PurgeAsync(Ct)).ShouldBe(1);
        Directory.Exists(folder).ShouldBeFalse();
        (await t.Db.ImportJobs.CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task An_invalid_edit_is_returned_without_saving()
    {
        await using var t = await RecipeTestScope.CreateAsync(web: web);
        var start = await t.Import.StartUrlAsync(PageUrl, false, RecipeTestScope.UserId, Ct);
        var result = await t.Import.SaveAsync(start.JobIds[0], new RecipeEdit { Title = new string('x', 300) }, false, false, RecipeTestScope.UserId, Ct);
        result.Outcome!.Status.ShouldBe(SaveStatus.Invalid);
        (await t.Db.ImportJobs.SingleAsync(Ct)).Status.ShouldBe(ImportStatus.Review);
        (await t.Import.GetReviewAsync(999, Ct)).ShouldBeNull();
    }

    public async ValueTask DisposeAsync() => await web.DisposeAsync();
}
