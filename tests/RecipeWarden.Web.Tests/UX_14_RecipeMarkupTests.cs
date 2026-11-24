using System.Net;
using System.Text.RegularExpressions;

namespace RecipeWarden.Web.Tests;

/// <summary>
/// UX-14, ADR 0048: the recipe page marks the recipe as a schema.org Recipe in one article, and keeps the page's
/// tools in asides, which reader modes leave out (the browser check is E2E UX_14_ReaderModeTests).
/// </summary>
[Trait("Req", "UX-14")]
public sealed partial class UX_14_RecipeMarkupTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_recipe_is_a_schema_org_recipe_and_the_tools_are_asides()
    {
        using var editor = await factory.SignInAsync("ux14-editor@example.com");
        var created = await editor.CreateRecipeAsync("Markup Chili", "1 lb beef\n2 cups beans", "Brown the beef.\nSimmer with beans.",
            extra: [("Input.Servings", "4"), ("Input.PrepTime", "15"), ("Input.CookTime", "1 hr")]);
        created.StatusCode.ShouldBe(HttpStatusCode.Redirect, await created.TextAsync());
        var html = WebUtility.HtmlDecode(await editor.GetStringAsync(created.Headers.Location!.ToString(), Ct));

        html.ShouldContain("itemscope itemtype=\"https://schema.org/Recipe\"");
        html.ShouldContain("<meta itemprop=\"name\" content=\"Markup Chili\">");
        Prop("recipeIngredient").Count(html).ShouldBe(2);
        Prop("recipeInstructions").Count(html).ShouldBe(2);
        html.ShouldContain("<meta itemprop=\"prepTime\" content=\"PT15M\">");
        html.ShouldContain("<meta itemprop=\"cookTime\" content=\"PT60M\">");
        html.ShouldContain("<meta itemprop=\"recipeYield\" content=\"4\">");

        // Every tool sits inside an aside (reader modes drop asides); the class name avoids "header".
        foreach (var tool in new[] { "Make this my default", "Add to shopping list", ">Cook mode<", ">Edit<" })
        {
            var at = html.IndexOf(tool, StringComparison.Ordinal);
            at.ShouldBeGreaterThan(0, tool);
            var asideOpen = html.LastIndexOf("<aside", at, StringComparison.Ordinal);
            var asideClose = html.LastIndexOf("</aside>", at, StringComparison.Ordinal);
            asideOpen.ShouldBeGreaterThan(asideClose, $"{tool} should be inside an aside");
        }

        html.ShouldNotContain("recipe-header");
    }

    [Fact]
    public async Task Ingredient_lines_are_paragraphs_and_the_page_says_it_is_an_article()
    {
        // Owner comparison with a page Safari Reader accepts (2026-11-23): every ingredient line is a <p>, and the head
        // carries og:type=article, og:title and a plain-text description.
        using var editor = await factory.SignInAsync("ux14-meta@example.com");
        var created = await editor.CreateRecipeAsync("Meta Chili", "1 lb beef\n2 cups beans\n1 onion, diced", "Brown the beef.",
            extra: [("Input.Description", "A **hearty** chili, ready in [an hour](https://example.com).")]);
        created.StatusCode.ShouldBe(HttpStatusCode.Redirect, await created.TextAsync());
        var html = WebUtility.HtmlDecode(await editor.GetStringAsync(created.Headers.Location!.ToString(), Ct));

        IngredientParagraph().Count(html).ShouldBe(3);
        html.ShouldContain("<meta property=\"og:type\" content=\"article\">");
        html.ShouldContain("<meta property=\"og:title\" content=\"Meta Chili\">");
        html.ShouldContain("<meta name=\"description\" content=\"A hearty chili, ready in an hour.\">");
        html.ShouldContain("<meta property=\"og:description\" content=\"A hearty chili, ready in an hour.\">");

        // Other pages are not articles.
        var home = WebUtility.HtmlDecode(await editor.GetStringAsync("/", Ct));
        home.ShouldNotContain("og:type");
    }

    [GeneratedRegex("""<li[^>]*>\s*<p class="ingredient-line"><span itemprop="recipeIngredient">""")]
    private static partial Regex IngredientParagraph();

    private static Regex Prop(string name) => new($"itemprop=\"{name}\"");
}
