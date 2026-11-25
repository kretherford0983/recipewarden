using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Recipes;
using SkiaSharp;

namespace RecipeWarden.E2E.Tests;

/// <summary>W3, W4, W2 steps 4–5 in a browser (FR-IMP-01..06, AC-10, AC-11, ADR 0022 to 0025).</summary>
[Trait("Req", "W3")]
[Trait("Req", "W4")]
public sealed class W3_W4_ImportTests(BrowserHost host) : IClassFixture<BrowserHost>
{
    private static byte[] Jpeg(int w, int h)
    {
        using var bitmap = new SKBitmap(w, h);
        bitmap.Erase(new SKColor(180, 110, 40));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        return data.ToArray();
    }

    [Fact]
    [Trait("Req", "AC-11")]
    public async Task W3_import_a_page_with_its_image_and_publish()
    {
        host.Web.Html("http://kitchen.example/chili", """
            <html><head><meta property="og:site_name" content="Example Kitchen">
            <script type="application/ld+json">{"@type":"Recipe","name":"Weeknight Chili","recipeYield":"6",
              "image":"http://cdn.example/chili.jpg","recipeIngredient":["1 lb ground beef","1 can (15 oz) beans"],
              "recipeInstructions":[{"@type":"HowToStep","text":"Brown the beef."},{"@type":"HowToStep","text":"Simmer with the beans."}]}</script>
            </head><body></body></html>
            """);
        host.Web.Serve("http://cdn.example/chili.jpg", 200, "image/jpeg", Jpeg(1200, 800));
        await host.CreateUserAsync("w3@example.com", AppRoles.Editor);
        await using var context = await host.NewContextAsync();
        var page = await context.NewPageAsync();
        await BrowserHost.SignInAsync(page, "w3@example.com");

        await page.GotoAsync("/manage/import");
        await page.GetByLabel("Page address").First.FillAsync("http://kitchen.example/chili");
        await page.GetByLabel("Also copy the finished-dish image").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Import", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/review");

        await Assertions.Expect(page.GetByLabel("Title")).ToHaveValueAsync("Weeknight Chili");
        await Assertions.Expect(page.Locator(".source")).ToContainTextAsync("Example Kitchen");
        await Assertions.Expect(page.GetByLabel("Copy the finished-dish image from the source")).ToBeCheckedAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Publish" }).ClickAsync();
        await page.WaitForURLAsync("**/recipes/weeknight-chili");

        var image = page.Locator("img.finish-image");
        await Assertions.Expect(image).ToBeVisibleAsync();
        (await image.EvaluateAsync<int>("img => img.naturalWidth")).ShouldBe(1200);
        await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Example Kitchen");
    }

    [Fact]
    public async Task W4_a_markdown_file_with_two_recipes_becomes_two_drafts()
    {
        await host.CreateUserAsync("w4@example.com", AppRoles.Editor);
        await using var context = await host.NewContextAsync();
        var page = await context.NewPageAsync();
        await BrowserHost.SignInAsync(page, "w4@example.com");
        var md = "# W4 Tea\n## Ingredients\n- 1 tea bag\n## Steps\n1. Steep.\n\n# W4 Coffee\n## Ingredients\n- 2 Tbsp coffee\n## Steps\n1. Brew.";

        await page.GotoAsync("/manage/import");
        await page.Locator("#files").SetInputFilesAsync(new FilePayload { Name = "drinks.md", MimeType = "text/markdown", Buffer = Encoding.UTF8.GetBytes(md) });
        await page.GetByRole(AriaRole.Button, new() { Name = "Import files" }).ClickAsync();
        await page.WaitForURLAsync("**/review");
        await Assertions.Expect(page.Locator(".import-batch")).ToContainTextAsync("This import found 2 recipes");

        await page.GetByRole(AriaRole.Button, new() { Name = "Save draft" }).ClickAsync();
        await page.WaitForURLAsync("**/edit?saved=draft");
        await page.GotoAsync("/manage/import");
        await page.GetByRole(AriaRole.Link, new() { Name = "W4 Coffee" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Save draft" }).ClickAsync();
        await page.WaitForURLAsync("**/edit?saved=draft");

        await page.GotoAsync("/recipes?status=draft");
        await Assertions.Expect(page.Locator(".recipe-grid")).ToContainTextAsync("W4 Tea");
        await Assertions.Expect(page.Locator(".recipe-grid")).ToContainTextAsync("W4 Coffee");
    }

    [Fact]
    [Trait("Req", "AC-144")]
    public async Task W4_the_review_bar_sits_inside_the_card_and_discards_from_the_bar()
    {
        await host.CreateUserAsync("w4-bar@example.com", AppRoles.Editor);
        await using var context = await host.NewContextAsync();
        var page = await context.NewPageAsync();
        await BrowserHost.SignInAsync(page, "w4-bar@example.com");
        var md = "# W4 Bar Sauce\n## Ingredients\n- 1 cup hot sauce\n- 1/3 cup oil\n## Steps\n1. Whisk.";

        await page.GotoAsync("/manage/import");
        await page.Locator("#files").SetInputFilesAsync(new FilePayload { Name = "bar.md", MimeType = "text/markdown", Buffer = Encoding.UTF8.GetBytes(md) });
        await page.GetByRole(AriaRole.Button, new() { Name = "Import files" }).ClickAsync();
        await page.WaitForURLAsync("**/review");
        var review = new Uri(page.Url).AbsolutePath;
        var folder = Environment.GetEnvironmentVariable("RW_SCREENSHOTS");

        await Assertions.Expect(page.Locator("#Input_Yield")).ToHaveValueAsync("about 1 1/3 cups");
        await Assertions.Expect(page.Locator("fieldset.review-options")).ToHaveCountAsync(0);
        await BarFitsAsync(page);
        await ShootAsync(page, folder, "review-bar");

        // The same with the AI marker (ADR 0042): the accent edge must not push the bar out of the card.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecipeWarden.Infrastructure.Data.AppDbContext>();
            var id = int.Parse(review.Split('/')[3], System.Globalization.CultureInfo.InvariantCulture);
            var job = await db.ImportJobs.FindAsync([id], TestContext.Current.CancellationToken);
            job!.ParsedByAi = true;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("form#recipe-editor.ai-parsed")).ToHaveCountAsync(1);
        await BarFitsAsync(page);
        await ShootAsync(page, folder, "review-bar-ai");

        await page.Locator(".sticky-actions").GetByRole(AriaRole.Button, new() { Name = "Discard this import" }).ClickAsync();
        await page.WaitForURLAsync("**/manage/import");
        await Assertions.Expect(page.Locator("main")).Not.ToContainTextAsync("W4 Bar Sauce");
    }

    private static async Task BarFitsAsync(IPage page)
    {
        var form = (await page.Locator("form#recipe-editor").BoundingBoxAsync())!;
        var bar = (await page.Locator("form#recipe-editor .sticky-actions").BoundingBoxAsync())!;
        bar.X.ShouldBeGreaterThanOrEqualTo(form.X, "the bar starts inside the card");
        (bar.X + bar.Width).ShouldBeLessThanOrEqualTo(form.X + form.Width + 0.5f, "the bar ends inside the card");
        var buttons = page.Locator(".sticky-actions button");
        await Assertions.Expect(buttons).ToHaveTextAsync(["Save draft", "Publish", "Discard this import"]);
    }

    private static async Task ShootAsync(IPage page, string? folder, string name)
    {
        if (folder is { Length: > 0 })
        {
            Directory.CreateDirectory(folder);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(folder, name + ".png"), FullPage = true });
        }
    }

    [Fact]
    [Trait("Req", "W2")]
    [Trait("Req", "ADR-0022")]
    public async Task W2_add_a_finish_photo_on_a_phone()
    {
        int id;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            id = (await scope.ServiceProvider.GetRequiredService<RecipeService>().CreateAsync(new RecipeEdit
            {
                Title = "Phone Photo Pie",
                IngredientsText = "1 crust",
                StepsText = "Bake.",
                WantsPublish = true,
            }, "seed", TestContext.Current.CancellationToken)).Id;
        }

        await host.CreateUserAsync("w2-photo@example.com", AppRoles.Editor);
        await using var context = await host.NewContextAsync(390, 844);
        var page = await context.NewPageAsync();
        await BrowserHost.SignInAsync(page, "w2-photo@example.com");

        await page.GotoAsync($"/manage/recipes/{id}/edit");
        await page.Locator("#finish-photo").SetInputFilesAsync(new FilePayload { Name = "pie.jpg", MimeType = "image/jpeg", Buffer = Jpeg(3000, 2000) });
        await page.Locator("#photos form").Filter(new() { Has = page.Locator("#finish-photo") }).GetByRole(AriaRole.Button, new() { Name = "Upload" }).ClickAsync();
        await page.WaitForURLAsync("**/edit#photos");
        await Assertions.Expect(page.GetByAltText("Finished dish")).ToBeVisibleAsync();

        await page.GotoAsync("/recipes/phone-photo-pie");
        var image = page.Locator("img.finish-image");
        await Assertions.Expect(image).ToBeVisibleAsync();
        (await image.EvaluateAsync<int>("img => img.naturalWidth")).ShouldBe(2400); // resized (ADR 0022)
        (await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth")).ShouldBeTrue();
    }
}
