using System.Globalization;
using System.Net;

namespace RecipeWarden.Web.Tests;

/// <summary>
/// FR-TOOL-04/05, BR-31, AC-41, AC-42, ADR 0017 through the pages. Each test uses its own factory because the
/// kitchen is shared by the whole site (a scale added in one test would change the others).
/// </summary>
[Trait("Req", "FR-TOOL-04")]
public sealed class FR_TOOL_04_SuggestionPageTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(RecipeWardenFactory Factory, HttpClient Editor, string Slug)> SetupAsync(string user)
    {
        var factory = new RecipeWardenFactory();
        var editor = await factory.SignInAsync(user);
        var response = await editor.CreateRecipeAsync("Banner Loaf", "2 cups all-purpose flour\n1 cup whole milk",
            extra: [("Input.IsPrecision", "true")]);
        var slug = response.Headers.Location!.ToString()["/recipes/".Length..];
        return (factory, editor, slug);
    }

    [Fact]
    [Trait("Req", "AC-41")]
    public async Task A_new_scale_shows_a_banner_and_reading_it_changes_nothing()
    {
        var (factory, editor, slug) = await SetupAsync("banner-1@example.com");
        await using var _ = factory;
        using var __ = editor;
        await editor.PostFormAsync("/manage/kitchen?handler=Preset", ("key", "scale"));
        var seen = await factory.SeenVersionAsync(slug);

        var html = await factory.ReadAsync($"/recipes/{slug}");
        html.ShouldContain("You now have a scale (Digital scale). Show 2 ingredients by weight?");
        html.ShouldContain("2 cups all-purpose flour → 250 g all-purpose flour");
        html.ShouldContain("Show me");
        html.ShouldNotContain("handler=Dismiss"); // readers cannot dismiss
        (await factory.SeenVersionAsync(slug)).ShouldBe(seen);

        var shown = await factory.ReadAsync($"/recipes/{slug}?weights=show");
        shown.ShouldContain("250 g all-purpose flour");
        shown.ShouldContain("Showing weights for this view.");
        shown.ShouldNotContain("Show 2 ingredients by weight?");
    }

    [Fact]
    [Trait("Req", "AC-41")]
    public async Task After_an_editor_dismisses_it_the_banner_does_not_return()
    {
        var (factory, editor, slug) = await SetupAsync("banner-2@example.com");
        await using var _ = factory;
        using var __ = editor;
        await editor.PostFormAsync("/manage/kitchen?handler=Preset", ("key", "scale"));
        var id = await factory.RecipeIdAsync(slug);
        var editorView = await factory.ReadAsync($"/recipes/{slug}", editor);
        editorView.ShouldContain("handler=Dismiss");
        var key = "weight:scale-" + (await factory.ToolIdAsync("Digital scale")).ToString(CultureInfo.InvariantCulture);

        (await editor.PostFormAsync($"/manage/recipes/{id}/suggestions?handler=Dismiss", ("key", key))).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        (await factory.ReadAsync($"/recipes/{slug}")).ShouldNotContain("by weight?");
        (await factory.ReadAsync($"/recipes/{slug}")).ShouldContain("2 cups all-purpose flour");
        (await editor.PostFormAsync($"/manage/recipes/{id}/suggestions?handler=Dismiss", ("key", "bogus"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Req", "FR-TOOL-05")]
    public async Task Save_to_recipe_makes_weights_the_default()
    {
        var (factory, editor, slug) = await SetupAsync("banner-3@example.com");
        await using var _ = factory;
        using var __ = editor;
        await editor.PostFormAsync("/manage/kitchen?handler=Preset", ("key", "scale"));
        var id = await factory.RecipeIdAsync(slug);
        (await factory.ReadAsync($"/manage/recipes/{id}/suggestions", editor)).ShouldContain("Save to recipe");
        var key = "weight:scale-" + (await factory.ToolIdAsync("Digital scale")).ToString(CultureInfo.InvariantCulture);

        (await editor.PostFormAsync($"/manage/recipes/{id}/suggestions?handler=Apply", ("key", key))).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var html = await factory.ReadAsync($"/recipes/{slug}");
        html.ShouldContain("250 g all-purpose flour");
        html.ShouldContain("(2 cups)");
        html.ShouldNotContain("by weight?");
    }

    [Fact]
    [Trait("Req", "AC-42")]
    public async Task A_new_spoon_shows_no_banner()
    {
        var (factory, editor, slug) = await SetupAsync("banner-4@example.com");
        await using var _ = factory;
        using var __ = editor;
        await editor.PostFormAsync("/manage/kitchen?handler=Preset", ("key", "eighth-tsp"));
        (await factory.ReadAsync($"/recipes/{slug}")).ShouldNotContain("suggestion-banner");
    }

    [Fact]
    public async Task Anonymous_suggestion_posts_are_refused()
    {
        await using var factory = new RecipeWardenFactory();
        using var client = factory.CreateHttpsClient();
        var response = await client.PostAsync("/manage/recipes/1/suggestions?handler=Dismiss", new StringContent(""), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldContain("/account/login");
    }
}
