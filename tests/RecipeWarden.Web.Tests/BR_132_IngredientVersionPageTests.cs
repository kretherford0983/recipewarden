using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using RecipeWarden.Core.Security;

namespace RecipeWarden.Web.Tests;

/// <summary>BR-132, AC-137: the review page offers three ways to resolve two versions of an ingredient group.</summary>
[Trait("Req", "FR-IMP-04")]
[Trait("Req", "BR-132")]
public sealed partial class BR_132_IngredientVersionPageTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly byte[] Corn = Encoding.UTF8.GetBytes(
        "# Street Corn\n## Ingredients\n### Corn\n- 2 tbsp olive oil\n- 32 oz frozen corn\n### Sauce (Side Dish)\n- 3 tbsp mayonnaise\n- 2 tbsp lime juice\n"
        + "### Sauce (Taco Topping)\n- 1/4 cup mayonnaise\n- 2.5 tbsp lime juice\n## Instructions\n1. Cook the corn.\n2. Stir in the sauce.\n");

    [GeneratedRegex(@"/manage/import/(\d+)/review")]
    private static partial Regex ReviewId();

    private static async Task<(string Url, string Id, string Html)> ImportAsync(HttpClient editor, string file)
    {
        var posted = await editor.PostFilesAsync("/manage/import?handler=Files", [("files", file, Corn)]);
        posted.StatusCode.ShouldBe(HttpStatusCode.Redirect, await posted.TextAsync());
        var url = posted.Headers.Location!.ToString();
        return (url, ReviewId().Match(url).Groups[1].Value, WebUtility.HtmlDecode(await editor.GetStringAsync(url, Ct)));
    }

    [Fact]
    [Trait("Req", "AC-137")]
    public async Task Keep_one_and_move_the_other_to_the_notes()
    {
        using var editor = await factory.SignInAsync("versions-1@example.com", AppRoles.Editor);
        var (url, id, html) = await ImportAsync(editor, "corn-1.md");
        html.ShouldContain("Two versions of “Sauce”");
        html.ShouldContain("Keep it, move the other to the notes");
        html.ShouldContain("Split into separate recipes");

        var moved = await editor.PostFormAsync($"/manage/import/{id}/review?handler=Versions",
            ("name", "Sauce"), ("keep", "Taco Topping"), ("choice", "MoveToNotes"));
        moved.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var after = WebUtility.HtmlDecode(await editor.GetStringAsync(url, Ct));
        after.ShouldNotContain("Two versions of “Sauce”");
        after.ShouldContain("- 3 tbsp mayonnaise");
        after.ShouldContain("Sauce (Taco Topping):");
    }

    [Fact]
    [Trait("Req", "AC-137")]
    public async Task Split_adds_a_second_draft_to_the_batch()
    {
        using var editor = await factory.SignInAsync("versions-2@example.com", AppRoles.Editor);
        var (url, id, _) = await ImportAsync(editor, "corn-2.md");
        var split = await editor.PostFormAsync($"/manage/import/{id}/review?handler=Versions",
            ("name", "Sauce"), ("keep", "Side Dish"), ("choice", "Split"));
        split.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var first = WebUtility.HtmlDecode(await editor.GetStringAsync(url, Ct));
        first.ShouldContain("Street Corn (Side Dish)");
        first.ShouldContain("This import found 2 recipes:");
        first.ShouldContain("Street Corn (Taco Topping)");
        first.ShouldNotContain("Two versions of “Sauce”");
    }

    [Fact]
    [Trait("Req", "AC-137")]
    public async Task Keep_only_drops_the_other_and_bad_input_is_refused()
    {
        using var editor = await factory.SignInAsync("versions-3@example.com", AppRoles.Editor);
        var (url, id, _) = await ImportAsync(editor, "corn-3.md");
        (await editor.PostFormAsync($"/manage/import/{id}/review?handler=Versions", ("name", "Sauce"), ("keep", "Dip"), ("choice", "KeepOnly")))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await editor.PostFormAsync($"/manage/import/{id}/review?handler=Versions", ("name", "Sauce"), ("keep", "Side Dish"), ("choice", "9")))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await editor.PostFormAsync($"/manage/import/{id}/review?handler=Versions", ("name", "Sauce"), ("keep", "Side Dish"), ("choice", "KeepOnly")))
            .StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var after = WebUtility.HtmlDecode(await editor.GetStringAsync(url, Ct));
        after.ShouldNotContain("Taco Topping");
        after.ShouldNotContain("This import found 2 recipes");
    }
}
