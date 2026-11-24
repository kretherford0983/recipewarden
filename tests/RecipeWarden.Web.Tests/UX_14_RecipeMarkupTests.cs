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

    private static Regex Prop(string name) => new($"itemprop=\"{name}\"");
}
