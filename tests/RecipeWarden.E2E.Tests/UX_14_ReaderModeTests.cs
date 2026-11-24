using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.E2E.Tests;

/// <summary>
/// UX-14, ADR 0048: the browser's reader mode (Safari Reader, Firefox Reader View, Chrome Reading mode) offers the
/// recipe page and shows the recipe, not the page's tools. Checked with Mozilla Readability, the library behind
/// Firefox Reader View (test-only, vendor/readability, Apache-2.0); Safari and Chrome use similar rules.
/// </summary>
[Trait("Req", "UX-14")]
public sealed class UX_14_ReaderModeTests(BrowserHost host) : IClassFixture<BrowserHost>
{
    private static readonly string Readerable = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "vendor", "readability", "Readability-readerable.js"));
    private static readonly string Readability = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "vendor", "readability", "Readability.js"));

    private const string ReadScript = """
        () => {
          const readerable = isProbablyReaderable(document);
          const article = new Readability(document.cloneNode(true)).parse();
          return JSON.stringify({ readerable, title: article ? article.title : "", text: article ? article.textContent : "" });
        }
        """;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record ReaderResult(bool Readerable, string Title, string Text);

    [Theory]
    [InlineData(null)]
    [InlineData("reader-editor@example.com")]
    public async Task The_recipe_page_is_offered_in_reader_mode_and_reads_as_the_recipe(string? editor)
    {
        var slug = await SeedAsync("Reader Beef Stroganoff " + (editor is null ? "A" : "E"));
        if (editor is not null)
        {
            await host.CreateUserAsync(editor, "Editor");
        }

        await using var context = await host.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = host.BaseUrl,
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
            BypassCSP = true, // only so the test can add the reference library to the page
            ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Forwarded-Proto"] = "https" },
        });
        var page = await context.NewPageAsync();
        if (editor is not null)
        {
            await BrowserHost.SignInAsync(page, editor);
        }

        await page.GotoAsync($"/recipes/{slug}");
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Content = Readerable });
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Content = Readability });
        var json = await page.EvaluateAsync<string>(ReadScript);
        var result = JsonSerializer.Deserialize<ReaderResult>(json, JsonOptions)!;

        result.Readerable.ShouldBeTrue("the browser should offer reader mode");
        result.Title.ShouldContain("Reader Beef Stroganoff");
        var text = result.Text;
        foreach (var wanted in new[]
                 {
                     "rich gravy",                       // description
                     "1 lb sirloin steak", "8 oz egg noodles", "For the sauce", "1 cup sour cream", // ingredients and group
                     "Slice the steak", "Serve over the noodles", // first and last step
                     "Leftovers keep",                   // notes
                 })
        {
            text.ShouldContain(wanted, Case.Insensitive, $"reader text should include \"{wanted}\"; it was: {Excerpt(text)}");
        }

        foreach (var tool in new[]
                 {
                     "Make this my default", "More scaling options", "Add to shopping list", "Cook mode", "Markdown + images",
                     "Export for AI", "I made this", "Nutrition and allergens", "Add to collection",
                 })
        {
            text.ShouldNotContain(tool, Case.Insensitive, $"reader text should not include the tool \"{tool}\"; it was: {Excerpt(text)}");
        }
    }

    private static string Excerpt(string text)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= 600 ? flat : flat[..600] + "…";
    }

    private async Task<string> SeedAsync(string title)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var saved = await scope.ServiceProvider.GetRequiredService<RecipeService>().CreateAsync(new RecipeEdit
        {
            Title = title,
            Description = "Tender strips of steak in a rich gravy thickened with sour cream, served over buttered egg noodles.",
            Servings = 4,
            PrepMinutes = 15,
            CookMinutes = 30,
            IngredientsText = "1 lb sirloin steak, thinly sliced\n8 oz egg noodles\n2 tbsp butter\n1 onion, diced\n"
                + "For the sauce:\n8 oz mushrooms, sliced\n2 cups beef broth\n1 cup sour cream\n1 tbsp flour",
            StepsText = "Slice the steak thinly across the grain and season it with salt and pepper.\n"
                + "Brown the steak in butter in a hot pan, in batches so it sears, then set it aside.\n"
                + "Cook the onion and mushrooms until soft, sprinkle in the flour and stir for a minute.\n"
                + "Pour in the broth, simmer until it thickens, then stir in the sour cream off the heat.\n"
                + "Return the steak to the pan to warm through. Serve over the noodles.",
            Notes = "Leftovers keep for three days in the fridge; reheat gently so the sauce does not split.",
            Tags = ["Dinner"],
            SourceName = "Family cookbook",
            WantsPublish = true,
        }, "seed", TestContext.Current.CancellationToken);
        return saved.Slug!;
    }
}
