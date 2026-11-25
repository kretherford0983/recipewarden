using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.AiParser;
using RecipeWarden.Core.Import;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Ai;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Import;
using RecipeWarden.Infrastructure.Media;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Tests.Shared;
using SkiaSharp;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>
/// FR-IMP-13, BR-128 to BR-131, SEC-48, AC-126 to AC-130, ADR 0058: photo import through the AI parser against a
/// pretend internet. The card pictures are drawn in memory (owner choice: no real household photos in the repo).
/// </summary>
[Trait("Req", "FR-IMP-13")]
public sealed class FR_IMP_13_PhotoImportServiceTests
{
    private const string ClaudeUrl = "http://api.anthropic.test/v1/messages";
    private const string LocalUrl = "http://192.168.1.50:11434/v1/chat/completions";
    private const string UserId = "user-1";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string CardMarkdown = """
        ---
        format: recipe-md/1
        title: "Aunt May's Oatmeal Cookies"
        servings: 24
        ---

        ## Ingredients

        - 1 cup butter
        - [unclear] cups brown sugar
        - 3 cups oats
        - 1 tsp [unclear]

        ## Steps

        1. Cream the butter and sugar.
        2. Stir in the oats and bake at 350°F for 12 minutes.
        """;

    private sealed class Scope : IAsyncDisposable
    {
        private readonly TestDatabase tdb;

        private Scope(TestDatabase tdb, FakeWeb web)
        {
            this.tdb = tdb;
            Web = web;
        }

        public FakeWeb Web { get; }

        public static async Task<Scope> CreateAsync()
        {
            var web = new FakeWeb();
            var tdb = await TestDatabase.CreateAsync(configure: s =>
            {
                s.AddSingleton<IDnsResolver>(web);
                s.AddSingleton<ISocketConnector>(web);
            }, settings: new Dictionary<string, string?> { ["Ai:ClaudeEndpoint"] = ClaudeUrl });
            return new Scope(tdb, web);
        }

        public async Task<T> WithAsync<T>(Func<IServiceProvider, Task<T>> work)
        {
            await using var scope = tdb.Provider.CreateAsyncScope();
            return await work(scope.ServiceProvider);
        }

        public Task<T> Import<T>(Func<ImportService, Task<T>> work) => WithAsync(sp => work(sp.GetRequiredService<ImportService>()));

        public Task<T> Db<T>(Func<AppDbContext, Task<T>> work) => WithAsync(sp => work(sp.GetRequiredService<AppDbContext>()));

        public Task<string?> ConfigureAsync(AiSettings settings, string? key = "fake-test-key-photo") =>
            WithAsync(sp => sp.GetRequiredService<AiParserService>().SaveSettingsAsync(settings, key, false, Ct));

        public void ClaudeReplies(string text) =>
            Web.Serve(ClaudeUrl, 200, "application/json", JsonSerializer.SerializeToUtf8Bytes(new
            {
                content = new[] { new { type = "text", text } },
                usage = new { input_tokens = 2600, output_tokens = 300 },
            }));

        public List<FakeWeb.Request> AiRequests =>
            Web.Requests.Where(r => r.Host.Contains("anthropic", StringComparison.Ordinal) || r.Host.StartsWith("192.168.", StringComparison.Ordinal)).ToList();

        public Task<ImportStart> StartAsync(IReadOnlyList<(string Name, byte[] Bytes)> photos, string? sourceName = null, bool keep = true) =>
            Import(i => i.StartPhotosAsync(photos.Select(p => new ImportFile(p.Name, new MemoryStream(p.Bytes), p.Bytes.Length)).ToList(),
                sourceName, keep, UserId, Ct));

        public async ValueTask DisposeAsync()
        {
            await tdb.DisposeAsync();
            await Web.DisposeAsync();
        }
    }

    private static AiSettings Claude(bool enabled = true, bool? readsImages = null, long? cap = null) =>
        new(enabled, AiProvider.Claude, null, "claude-haiku-5-5", cap, false) { ReadsImages = readsImages };

    /// <summary>An invented, card-shaped picture: cream paper with a few dark "handwriting" strokes.</summary>
    private static byte[] Card(int width = 1200, int height = 800, SKEncodedImageFormat format = SKEncodedImageFormat.Jpeg)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(250, 240, 215));
            using var ink = new SKPaint { Color = new SKColor(30, 30, 90), StrokeWidth = Math.Max(2, width / 300f), IsAntialias = true };
            for (var line = 1; line <= 6; line++)
            {
                var y = height * line / 8f;
                canvas.DrawLine(width * 0.08f, y, width * (0.5f + (line % 3 * 0.12f)), y + 4, ink);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    /// <summary>Random pixels: JPEG cannot shrink them, so a few of these overflow the 4 MB request cap.</summary>
    private static byte[] Noise(int side)
    {
        var random = new Random(side);
        using var bitmap = new SKBitmap(side, side);
        var pixels = new byte[side * side * 4];
        random.NextBytes(pixels);
        for (var i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
        }

        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        return data.ToArray();
    }

    /// <summary>The JPEGs a captured request carries, in order (Claude or OpenAI-compatible shape).</summary>
    private static List<byte[]> SentImages(FakeWeb.Request request)
    {
        using var doc = JsonDocument.Parse(request.Body);
        var messages = doc.RootElement.GetProperty("messages");
        var content = messages[messages.GetArrayLength() - 1].GetProperty("content");
        var images = new List<byte[]>();
        foreach (var part in content.EnumerateArray())
        {
            if (part.GetProperty("type").GetString() == "image")
            {
                images.Add(Convert.FromBase64String(part.GetProperty("source").GetProperty("data").GetString()!));
            }
            else if (part.GetProperty("type").GetString() == "image_url")
            {
                var url = part.GetProperty("image_url").GetProperty("url").GetString()!;
                url.ShouldStartWith("data:image/jpeg;base64,");
                images.Add(Convert.FromBase64String(url["data:image/jpeg;base64,".Length..]));
            }
        }

        return images;
    }

    private static RecipeEdit EditFrom(ImportReview review, string? ingredients = null) => new()
    {
        Title = review.Recipe.Title,
        Servings = 24,
        IngredientsText = ingredients ?? review.Recipe.Ingredients,
        StepsText = review.Recipe.Steps,
        SourceType = review.Recipe.SourceType ?? SourceType.Manual,
        SourceName = review.Recipe.SourceName,
        WantsPublish = true,
    };

    [Fact]
    [Trait("Req", "AC-126")]
    [Trait("Req", "BR-130")]
    public async Task With_the_parser_off_or_a_model_that_cannot_read_images_nothing_is_sent()
    {
        await using var s = await Scope.CreateAsync();
        s.ClaudeReplies(CardMarkdown);

        (await s.ConfigureAsync(Claude(enabled: false))).ShouldBeNull();
        var off = await s.StartAsync([("card.jpg", Card())]);
        off.Error.ShouldBe(AiParserRules.Disabled);
        (await s.WithAsync(sp => sp.GetRequiredService<AiParserService>().PhotoImportUnavailableAsync(Ct))).ShouldBe(AiParserRules.Disabled);

        (await s.ConfigureAsync(Claude(readsImages: false))).ShouldBeNull();
        (await s.StartAsync([("card.jpg", Card())])).Error.ShouldBe(PhotoImportRules.NoImages);

        s.AiRequests.ShouldBeEmpty();
        s.Web.Lookups.ShouldBeEmpty();
        (await s.Db(db => db.ImportJobs.CountAsync(Ct))).ShouldBe(0);
    }

    [Fact]
    [Trait("Req", "AC-127")]
    [Trait("Req", "SEC-48")]
    public async Task The_photo_sent_is_an_upright_small_jpeg_without_metadata()
    {
        await using var s = await Scope.CreateAsync();
        s.ClaudeReplies(CardMarkdown);
        (await s.ConfigureAsync(Claude())).ShouldBeNull();
        var phone = TestImages.JpegWithExif(6, "GPS-SECRET-DESCRIPTION", 4032, 3024); // 6 = rotate 90° clockwise to view

        var start = await s.StartAsync([("IMG_0042.jpg", phone)]);

        start.Error.ShouldBeNull();
        var sent = SentImages(s.AiRequests.ShouldHaveSingleItem()).ShouldHaveSingleItem();
        using var codec = SKCodec.Create(new MemoryStream(sent));
        codec.EncodedFormat.ShouldBe(SKEncodedImageFormat.Jpeg);
        codec.EncodedOrigin.ShouldBe(SKEncodedOrigin.TopLeft);
        codec.Info.Width.ShouldBe(1176, "turned upright: 3024 × 4032 scaled to a 1568 px long edge");
        codec.Info.Height.ShouldBe(PhotoImportRules.MaxSide);
        Encoding.Latin1.GetString(sent).ShouldNotContain("Exif");
        Encoding.Latin1.GetString(sent).ShouldNotContain("GPS-SECRET");
        s.AiRequests[0].Body.ShouldNotContain("IMG_0042", customMessage: "the file name is not sent (SEC-46)");

        var folder = Path.Combine(await s.WithAsync(sp => Task.FromResult(sp.GetRequiredService<DataOptions>().ImportsDirectory)), start.BatchId!);
        Directory.GetFiles(folder).Select(Path.GetFileName).Order().ShouldBe(["photo-0.jpg", "photo-0.webp"]); // the original upload is not stored
    }

    [Fact]
    [Trait("Req", "AC-128")]
    [Trait("Req", "BR-128")]
    [Trait("Req", "BR-131")]
    public async Task Two_photos_go_in_order_and_become_a_marked_draft_for_review()
    {
        await using var s = await Scope.CreateAsync();
        s.ClaudeReplies(CardMarkdown);
        (await s.ConfigureAsync(Claude())).ShouldBeNull();

        var start = await s.StartAsync([("front.jpg", Card(1200, 800)), ("back.png", Card(600, 900, SKEncodedImageFormat.Png))], "Aunt May's card");

        start.Error.ShouldBeNull();
        var request = s.AiRequests.ShouldHaveSingleItem();
        request.Body.ShouldContain("[unclear]", customMessage: "the photo instructions");
        request.Body.ShouldContain("2 photos");
        request.Body.ShouldNotContain("Aunt May's card", customMessage: "only the photos and instructions are sent (SEC-46)");
        var sent = SentImages(request);
        sent.Count.ShouldBe(2);
        SKBitmap.DecodeBounds(sent[0]).Width.ShouldBe(1200);
        SKBitmap.DecodeBounds(sent[1]).Height.ShouldBe(900);

        var review = (await s.Import(i => i.GetReviewAsync(start.JobIds.ShouldHaveSingleItem(), Ct))).ShouldNotBeNull();
        review.Kind.ShouldBe(ImportSourceKind.Photo);
        review.ParsedByAi.ShouldBeTrue();
        review.PhotoCount.ShouldBe(2);
        review.KeepPhotos.ShouldBeTrue();
        review.Recipe.Title.ShouldBe("Aunt May's Oatmeal Cookies");
        review.Recipe.SourceType.ShouldBe(SourceType.Personal);
        review.Recipe.SourceName.ShouldBe("Aunt May's card");
        review.Recipe.SourceUrl.ShouldBeNull();
        review.Warnings.Where(w => w.Message == PhotoImportRules.UnclearWarning).Select(w => w.Line).ShouldBe(["[unclear] cups brown sugar", "1 tsp [unclear]"]);
        (await s.Db(db => db.Recipes.CountAsync(Ct))).ShouldBe(0, "nothing is saved until the Editor confirms");
        (await s.Import(i => i.PhotoPathAsync(review.JobId, 1, Ct))).ShouldNotBeNull().ShouldEndWith("photo-1.jpg");
        (await s.Import(i => i.PhotoPathAsync(review.JobId, 2, Ct))).ShouldBeNull();

        var log = await s.Db(db => db.AiCallLogs.AsNoTracking().SingleAsync(Ct));
        log.Purpose.ShouldBe(AiPurpose.PhotoImport);
        log.InputTokens.ShouldBe(2600);
    }

    [Fact]
    [Trait("Req", "AC-128")]
    public async Task A_local_vision_model_gets_image_url_parts_and_re_parse_sends_the_kept_photos_again()
    {
        await using var s = await Scope.CreateAsync();
        s.Web.Serve(LocalUrl, 200, "application/json", JsonSerializer.SerializeToUtf8Bytes(new
        {
            choices = new[] { new { message = new { role = "assistant", content = CardMarkdown } } },
            usage = new { prompt_tokens = 900, completion_tokens = 200 },
        }));
        (await s.ConfigureAsync(new AiSettings(true, AiProvider.OpenAiCompatible, "http://192.168.1.50:11434", "qwen2.5vl", null, true) { ReadsImages = true },
            key: null)).ShouldBeNull();

        var start = await s.StartAsync([("card.webp", Card(800, 600, SKEncodedImageFormat.Webp))]);

        start.Error.ShouldBeNull();
        SentImages(s.AiRequests.ShouldHaveSingleItem()).ShouldHaveSingleItem();
        (await s.Import(i => i.ReparseWithAiAsync(start.JobIds[0], Ct))).ShouldBeNull();
        s.AiRequests.Count.ShouldBe(2);
        SentImages(s.AiRequests[1]).ShouldHaveSingleItem().ShouldBe(SentImages(s.AiRequests[0])[0]);
        (await s.Db(db => db.AiCallLogs.AsNoTracking().OrderBy(l => l.Id).Select(l => l.Purpose).ToListAsync(Ct))).ShouldBe([AiPurpose.PhotoImport, AiPurpose.Reparse]);
    }

    [Fact]
    [Trait("Req", "AC-129")]
    [Trait("Req", "BR-129")]
    public async Task Saving_keeps_the_card_photos_in_order_unless_unticked_and_unclear_amounts_block_it()
    {
        await using var s = await Scope.CreateAsync();
        s.ClaudeReplies(CardMarkdown);
        (await s.ConfigureAsync(Claude())).ShouldBeNull();
        var start = await s.StartAsync([("front.jpg", Card(1200, 800)), ("back.jpg", Card(700, 500))], "Aunt May's card");
        var review = (await s.Import(i => i.GetReviewAsync(start.JobIds[0], Ct)))!;

        var blocked = await s.Import(i => i.SaveAsync(review.JobId, EditFrom(review), false, false, true, UserId, Ct));
        blocked.Outcome!.Status.ShouldBe(SaveStatus.Invalid);
        blocked.Outcome.Errors.ShouldHaveSingleItem().Message.ShouldContain("[unclear] cups brown sugar");
        (await s.Db(db => db.Recipes.CountAsync(Ct))).ShouldBe(0);

        var fixedLines = "1 cup butter\n2 cups brown sugar\n3 cups oats\n1 tsp [unclear]";
        var saved = await s.Import(i => i.SaveAsync(review.JobId, EditFrom(review, fixedLines), false, false, true, UserId, Ct));
        saved.Outcome!.Status.ShouldBe(SaveStatus.Saved, "an [unclear] outside the amount only warns");

        var cards = await s.Db(db => db.RecipeCardImages.AsNoTracking().Where(c => c.RecipeId == saved.Outcome.Id).OrderBy(c => c.Position).ToListAsync(Ct));
        cards.Count.ShouldBe(2);
        var first = (await s.WithAsync(sp => sp.GetRequiredService<ImageStore>().FindAsync(cards[0].ImageId, false, canSeeAll: false, Ct))).ShouldNotBeNull(
            "a published recipe's card is public like its other images");
        SKBitmap.DecodeBounds(await File.ReadAllBytesAsync(first.Path, Ct)).Width.ShouldBe(1200);
        (await s.WithAsync(sp => sp.GetRequiredService<ImageStore>().PurgeOrphansAsync(Ct))).ShouldBe(0);

        await using (var zip = new MemoryStream())
        {
            (await s.WithAsync(sp => sp.GetRequiredService<RecipeExporter>().WriteZipAsync(zip, saved.Outcome.Id, null, Ct))).ShouldBeTrue();
            zip.Position = 0;
            using var archive = new ZipArchive(zip);
            archive.Entries.Select(e => e.FullName).ShouldContain("card-1.webp");
            archive.Entries.Select(e => e.FullName).ShouldContain("card-2.webp");
        }

        var second = await s.StartAsync([("front.jpg", Card())], keep: true);
        var review2 = (await s.Import(i => i.GetReviewAsync(second.JobIds[0], Ct)))!;
        var unticked = await s.Import(i => i.SaveAsync(review2.JobId, EditFrom(review2, fixedLines) with { Title = "Cookies two" }, false, true, false, UserId, Ct));
        unticked.Outcome!.Status.ShouldBe(SaveStatus.Saved);
        (await s.Db(db => db.RecipeCardImages.CountAsync(c => c.RecipeId == unticked.Outcome.Id, Ct))).ShouldBe(0);
    }

    [Fact]
    [Trait("Req", "AC-130")]
    [Trait("Req", "SEC-50")]
    public async Task Too_many_wrong_type_too_big_or_over_the_request_cap_are_refused_before_connecting()
    {
        await using var s = await Scope.CreateAsync();
        s.ClaudeReplies(CardMarkdown);
        (await s.ConfigureAsync(Claude())).ShouldBeNull();

        (await s.StartAsync(Enumerable.Range(0, 5).Select(i => ($"p{i}.jpg", Card(300, 200))).ToList())).Error.ShouldBe(PhotoImportRules.TooMany);
        var heic = "\0\0\0\u0018ftypheic\0\0\0\0mif1heic"u8.ToArray().Concat(new byte[200]).ToArray();
        (await s.StartAsync([("IMG_1.heic", heic)])).Error.ShouldNotBeNull().ShouldContain("JPEG");
        var big = new byte[11 * 1024 * 1024];
        Card().CopyTo(big, 0);
        (await s.StartAsync([("big.jpg", big)])).Error.ShouldNotBeNull().ShouldContain("10 MB");
        (await s.StartAsync([.. Enumerable.Range(0, 4).Select(i => ($"n{i}.jpg", Noise(1700 + i)))])).Error.ShouldBe(PhotoImportRules.TooLarge);

        s.AiRequests.ShouldBeEmpty();
        s.Web.Connections.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Req", "AC-130")]
    [Trait("Req", "FR-IMP-10")]
    public async Task At_the_monthly_cap_photo_import_says_so_and_sends_nothing()
    {
        await using var s = await Scope.CreateAsync();
        s.ClaudeReplies(CardMarkdown);
        (await s.ConfigureAsync(Claude(cap: 1000))).ShouldBeNull();
        (await s.StartAsync([("card.jpg", Card())])).Error.ShouldBeNull("2,900 tokens used now");
        s.AiRequests.Count.ShouldBe(1);

        (await s.StartAsync([("card.jpg", Card())])).Error.ShouldBe(PhotoImportRules.CapReached);
        s.AiRequests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_image_test_passes_when_the_model_reads_the_word()
    {
        await using var s = await Scope.CreateAsync();
        (await s.ConfigureAsync(Claude())).ShouldBeNull();
        s.ClaudeReplies("TOAST");
        (await s.WithAsync(sp => sp.GetRequiredService<AiParserService>().TestImagesAsync(Ct))).ShouldBeNull();
        SentImages(s.AiRequests.ShouldHaveSingleItem()).ShouldHaveSingleItem();

        s.ClaudeReplies("I can't see any image.");
        (await s.WithAsync(sp => sp.GetRequiredService<AiParserService>().TestImagesAsync(Ct))).ShouldNotBeNull().ShouldContain("could not read");
    }
}
