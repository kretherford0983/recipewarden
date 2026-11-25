using System.Net;

namespace RecipeWarden.Web.Tests;

/// <summary>ADR 0015, FR-REC-02, SEC-22: pasted numbered steps with bold headings, shown and edited without raw asterisks.</summary>
[Trait("Req", "FR-REC-02")]
public sealed class ADR_0015_StepHeadingPageTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_owner_paste_shows_one_step_with_a_heading_and_round_trips_in_the_editor()
    {
        using var editor = await factory.SignInAsync("steps-editor@example.com");
        var slug = await editor.AddRecipeAsync("Heading Bake", steps: "1. **Preheat oven**\n   Preheat oven to **375°F**.\n2. **Bake**\n   Bake 20 minutes.");

        var page = WebUtility.HtmlDecode(await editor.GetStringAsync("/recipes/" + slug, Ct));
        page.ShouldContain("<strong class=\"step-title\">Preheat oven</strong>");
        page.ShouldContain("Preheat oven to 375°F.");
        page.ShouldNotContain("**");

        var id = await factory.RecipeIdAsync(slug);
        var form = WebUtility.HtmlDecode(await editor.GetStringAsync($"/manage/recipes/{id}/edit", Ct));
        form.ShouldContain("1. **Preheat oven**\n   Preheat oven to 375°F.\n2. **Bake**\n   Bake 20 minutes.");
    }

    [Fact]
    [Trait("Req", "SEC-22")]
    public async Task Step_headings_are_encoded()
    {
        using var editor = await factory.SignInAsync("steps-xss@example.com");
        var slug = await editor.AddRecipeAsync("Heading Xss", steps: "1. **<img src=x onerror=alert(1)>**\n   Then this.");
        var html = await editor.GetStringAsync("/recipes/" + slug, Ct);
        html.ShouldNotContain("<img src=x");
        html.ShouldContain("&lt;img src=x");
    }
}
