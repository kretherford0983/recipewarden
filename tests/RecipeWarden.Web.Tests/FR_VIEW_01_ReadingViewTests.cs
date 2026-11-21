using System.Net;

namespace RecipeWarden.Web.Tests;

/// <summary>FR-VIEW-01, UX-11, BR-10, AC-23 (no edit controls for readers; cook mode is M5), ADR 0008/0009.</summary>
[Trait("Req", "FR-VIEW-01")]
public sealed class FR_VIEW_01_ReadingViewTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Req", "AC-23")]
    [Trait("Req", "UX-11")]
    public async Task Anonymous_reader_sees_the_recipe_and_no_edit_controls()
    {
        using var editor = await factory.SignInAsync("view-1@example.com");
        var response = await editor.CreateRecipeAsync("Lemon Bars", "For the crust:\n1 cup flour\n½ cup butter, softened\nFor the filling:\n2 eggs",
            "Crust:\nPress into pan.\nFilling:\nWhisk and pour.", "publish", "dessert",
            ("Input.Description", "Tart and **sweet**."), ("Input.Notes", "Chill before cutting."), ("Input.Servings", "16"),
            ("Input.PrepMinutes", "20"), ("Input.CookMinutes", "45"), ("Input.SourceType", "Web"),
            ("Input.SourceName", "Example Kitchen"), ("Input.SourceUrl", "https://example.com/lemon-bars"), ("Input.SourceAuthor", "Jane"));
        var slug = response.Headers.Location!.ToString()["/recipes/".Length..];

        using var reader = factory.CreateHttpsClient();
        var page = await reader.GetAsync("/recipes/" + slug, Ct);
        page.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await page.TextAsync();

        html.ShouldContain("<h1 id=\"recipe-title\">Lemon Bars</h1>");
        html.ShouldContain("<strong>sweet</strong>");
        html.ShouldContain("<h3>For the crust</h3>");
        html.ShouldContain("½ cup butter, softened");
        html.ShouldContain("<h3>Filling</h3>");
        html.ShouldContain("Whisk and pour.");
        html.ShouldContain("Chill before cutting.");
        html.ShouldContain("<dd>65 min</dd>");
        html.ShouldContain("<dd>16</dd>");
        html.ShouldContain("href=\"/tags/dessert\"");
        // BR-10: credit with a direct link that does not leak the referrer or the opener.
        html.ShouldContain("<a href=\"https://example.com/lemon-bars\" rel=\"noopener noreferrer nofollow\">Example Kitchen</a>");
        html.ShouldContain("by Jane");
        // UX-11: readers never see edit controls.
        html.ShouldNotContain("/manage/recipes/");
        html.ShouldNotContain("Add to collection");
        html.ShouldNotContain(">Manage<");

        var asEditor = await editor.GetStringAsync("/recipes/" + slug, Ct);
        asEditor.ShouldContain("/manage/recipes/");
        asEditor.ShouldContain(">Edit</a>");
    }

    [Fact]
    public async Task Drafts_are_404_for_readers_and_marked_for_editors()
    {
        using var editor = await factory.SignInAsync("view-2@example.com");
        await editor.CreateRecipeAsync("Secret Sauce", steps: "", action: "draft");

        using var reader = factory.CreateHttpsClient();
        (await reader.GetAsync("/recipes/secret-sauce", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetStringAsync("/recipes?status=draft", Ct)).ShouldNotContain("Secret Sauce"); // status ignored for readers

        (await editor.GetStringAsync("/recipes/secret-sauce", Ct)).ShouldContain("Draft — only editors can see this recipe");
        (await editor.GetStringAsync("/recipes?status=draft", Ct)).ShouldContain("Secret Sauce");
    }

    [Fact]
    [Trait("Req", "BR-02")]
    public async Task Old_slug_redirects_permanently_after_a_title_change()
    {
        using var editor = await factory.SignInAsync("view-3@example.com");
        var slug = await editor.AddRecipeAsync("Plain Rice");
        var id = await factory.RecipeIdAsync(slug);
        await editor.PostFormAsync($"/manage/recipes/{id}/edit", ("Input.Title", "Fragrant Rice"), ("Input.Ingredients", "1 cup flour"),
            ("Input.Steps", "Mix."), ("Input.SourceType", "Manual"), ("Input.Version", "1"), ("Action", "publish"));

        using var reader = factory.CreateHttpsClient();
        var old = await reader.GetAsync("/recipes/plain-rice", Ct);
        old.StatusCode.ShouldBe(HttpStatusCode.MovedPermanently);
        old.Headers.Location!.ToString().ShouldBe("/recipes/fragrant-rice");
    }

    [Theory]
    [InlineData("/recipes/does-not-exist")]
    [InlineData("/recipes/BAD%20SLUG")]
    [InlineData("/recipes/..%2F..%2Fetc")]
    public async Task Unknown_or_malformed_slugs_are_404(string path)
    {
        using var reader = factory.CreateHttpsClient();
        (await reader.GetAsync(path, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Req", "FR-HOME-01")]
    [Trait("Req", "UX-09")]
    public async Task Home_lists_recent_recipes_collections_and_tags()
    {
        using var editor = await factory.SignInAsync("view-4@example.com");
        await editor.AddRecipeAsync("Homepage Hash", tags: "breakfast");

        using var reader = factory.CreateHttpsClient();
        var home = await reader.GetStringAsync("/", Ct);
        home.ShouldContain("Recently added");
        home.ShouldContain("Homepage Hash");
        home.ShouldContain("href=\"/tags/breakfast\"");
    }
}
