using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Media;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Infrastructure.Shopping;
using SkiaSharp;

namespace RecipeWarden.E2E.Tests;

/// <summary>
/// ADR 0047, docs/ui-standards.md: the main screens in both themes, on a desktop, on a phone and in Safari's HomeView window (448×364). Every screen must fit
/// its window (UX-01: nothing wider than the screen). When RW_SCREENSHOTS names a folder, a screenshot of each screen
/// is written there for a visual review (docs/screenshots is refreshed this way).
/// </summary>
[Trait("Req", "UX-01")]
[Trait("Req", "UX-03")]
[Trait("Req", "UX-13")]
public sealed class UX_ScreenshotTests(BrowserHost host) : IClassFixture<BrowserHost>
{
    private const string Admin = "screens-admin@example.com";

    private static readonly (string Name, string Path)[] Screens =
    [
        ("home", "/"),
        ("browse", "/recipes"),
        ("recipe", "/recipes/lemon-herb-roast-chicken"),
        ("cook", "/recipes/lemon-herb-roast-chicken/cook"),
        ("editor", "/manage/recipes/new"),
        ("shopping", "/shopping"),
        ("manage", "/manage"),
        ("tags", "/manage/tags"),
        ("admin", "/admin"),
        ("users", "/admin/users"),
        ("settings", "/admin/settings"),
    ];

    [Fact]
    public async Task Main_screens_fit_their_window_in_both_themes()
    {
        await SeedAsync();
        var folder = Environment.GetEnvironmentVariable("RW_SCREENSHOTS");
        if (folder is { Length: > 0 })
        {
            Directory.CreateDirectory(folder);
        }

        foreach (var (device, width, height) in new[] { ("desktop", 1280, 860), ("phone", 390, 844), ("homeview", 448, 364) })
        {
            foreach (var scheme in new[] { ColorScheme.Light, ColorScheme.Dark })
            {
                await using var context = await host.Browser.NewContextAsync(new BrowserNewContextOptions
                {
                    BaseURL = host.BaseUrl,
                    ViewportSize = new ViewportSize { Width = width, Height = height },
                    ColorScheme = scheme,
                    ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Forwarded-Proto"] = "https" },
                });
                var page = await context.NewPageAsync();

                await page.GotoAsync("/account/login");
                await Shoot(page, folder, $"{device}-{Label(scheme)}-login");
                await BrowserHost.SignInAsync(page, Admin);

                foreach (var (name, path) in Screens)
                {
                    var response = await page.GotoAsync(path);
                    response.ShouldNotBeNull();
                    response.Ok.ShouldBeTrue($"{path} answered {response.Status}");
                    await Shoot(page, folder, $"{device}-{Label(scheme)}-{name}");
                    var widest = await page.EvaluateAsync<string>(WidestScript);
                    widest.ShouldBeEmpty($"{path} is wider than a {width} px window");
                }
            }
        }
    }

    /// <summary>Empty when the page fits; otherwise the elements that stick out past the window's right edge.</summary>
    private const string WidestScript = """
        () => {
          if (document.documentElement.scrollWidth <= window.innerWidth) return "";
          const out = [];
          for (const e of document.querySelectorAll("main *")) {
            const r = e.getBoundingClientRect();
            if (r.right > window.innerWidth + 1 && r.width > 0) out.push(e.tagName.toLowerCase() + (e.id ? "#" + e.id : "") + "." + [...e.classList].join(".") + " " + Math.round(r.right));
            if (out.length > 5) break;
          }
          return out.join(", ") || "(outside main)";
        }
        """;

    private static string Label(ColorScheme scheme) => scheme == ColorScheme.Dark ? "dark" : "light";

    private static async Task Shoot(IPage page, string? folder, string name)
    {
        if (folder is not { Length: > 0 })
        {
            return;
        }

        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(folder, name + ".png"), FullPage = true });
    }

    private async Task SeedAsync()
    {
        await host.CreateUserAsync(Admin, "Admin");
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var recipes = services.GetRequiredService<RecipeService>();
        var images = services.GetRequiredService<ImageStore>();
        var collections = services.GetRequiredService<CollectionService>();
        var shopping = services.GetRequiredService<ShoppingService>();
        var token = TestContext.Current.CancellationToken;

        (string Title, string Tags, SKColor From, SKColor To)[] seed =
        [
            ("Lemon Herb Roast Chicken", "Dinner,Chicken", new SKColor(0xE8, 0xB4, 0x4C), new SKColor(0x9A, 0x4B, 0x1C)),
            ("Creamy White Chicken Chili", "Soup,Chicken,Slow cooker", new SKColor(0xF2, 0xE6, 0xC8), new SKColor(0x8C, 0x9A, 0x5B)),
            ("Weeknight Green Curry", "Thai,Dinner", new SKColor(0x9C, 0xC0, 0x5A), new SKColor(0x3E, 0x6B, 0x2E)),
            ("Brown Butter Banana Bread", "Baking,Breakfast", new SKColor(0xD9, 0xA0, 0x62), new SKColor(0x6B, 0x3A, 0x1E)),
            ("Tomato Basil Soup", "Soup,Vegetarian", new SKColor(0xE5, 0x5A, 0x3C), new SKColor(0x8E, 0x1F, 0x14)),
            ("Overnight Oats", "Breakfast,Vegetarian", new SKColor(0xF0, 0xE2, 0xCB), new SKColor(0xA8, 0x7C, 0x5A)),
        ];

        var collection = await collections.CreateAsync("Family favourites", "The ones we make every month.", "seed", token);
        var list = await shopping.CreateAsync("This week", "seed", token);
        foreach (var (title, tags, from, to) in seed)
        {
            var saved = await recipes.CreateAsync(new RecipeEdit
            {
                Title = title,
                Description = "A dependable family recipe with simple ingredients and **big flavour**.",
                Servings = 4,
                PrepMinutes = 15,
                CookMinutes = 45,
                IngredientsText = "- 1 whole chicken (about 4 lb)\n• 2 lemons, zested and halved\n4 cloves garlic, smashed\n"
                    + "2 tbsp olive oil\n1 tbsp fresh thyme leaves\n1 1/2 tsp kosher salt\n1/2 tsp black pepper\n1 lb small potatoes, halved",
                StepsText = "Heat the oven to 425°F and set a rack in the middle.\n"
                    + "Pat the chicken dry. Rub with oil, lemon zest, thyme, salt and pepper.\n"
                    + "Stuff the cavity with the lemon halves and garlic.\n"
                    + "Scatter the potatoes around the chicken in a roasting pan.\n"
                    + "Roast for 1 hour 15 minutes, until the thickest part of the thigh reads 165°F.\n"
                    + "Rest for 10 minutes before carving.",
                Tags = tags.Split(','),
                SourceName = "Family cookbook",
                WantsPublish = true,
            }, "seed", token);
            var (imageId, error) = await images.SaveAsync(Photo(from, to), null, "seed", token);
            error.ShouldBeNull();
            (await recipes.SetFinishImageAsync(saved.Id, imageId, "seed", token)).ShouldBeTrue();
            await collections.AddRecipeAsync(collection.Id, saved.Id, token);
            if (list.Id is int listId && saved.Slug is { } slug && title.Contains("Chicken", StringComparison.Ordinal))
            {
                await shopping.AddRecipeAsync(listId, slug, 1m, token);
            }
        }
    }

    /// <summary>A stand-in "photo": a diagonal gradient with a plate, encoded as PNG (no real images in the repo).</summary>
    private static byte[] Photo(SKColor from, SKColor to)
    {
        using var bitmap = new SKBitmap(800, 600);
        using (var canvas = new SKCanvas(bitmap))
        {
            using var background = new SKPaint();
            background.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(800, 600), [from, to], SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, 800, 600, background);
            using var plate = new SKPaint { Color = new SKColor(255, 255, 255, 70), IsAntialias = true };
            canvas.DrawCircle(400, 310, 210, plate);
            using var food = new SKPaint { Color = to.WithAlpha(200), IsAntialias = true };
            canvas.DrawCircle(400, 310, 120, food);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }
}
