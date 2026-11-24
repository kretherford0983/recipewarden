using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using RecipeWarden.Core.Security;

namespace RecipeWarden.Web.Tests;

/// <summary>BR-125, AC-118: a batch size folded into the notes can still become its own draft from the review page.</summary>
[Trait("Req", "FR-IMP-04")]
[Trait("Req", "BR-125")]
public sealed partial class BR_125_FoldedBatchPageTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"/manage/import/(\d+)/review")]
    private static partial Regex ReviewId();

    [Fact]
    [Trait("Req", "AC-118")]
    public async Task The_double_batch_is_in_the_notes_and_can_be_split_out()
    {
        using var editor = await factory.SignInAsync("fold-1@example.com", AppRoles.Editor);
        var md = Encoding.UTF8.GetBytes("# Corn Bake (Single Batch)\n## Ingredients\n- 1 can corn\n- 2 eggs\n- 12 oz cheese\n## Instructions\n1. Bake 30 minutes.\n\n"
                                        + "# Corn Bake (Double Batch)\n## Ingredients\n- 2 cans corn\n- 4 eggs\n- 24 oz cheese\n## Instructions\n1. Bake 70 minutes, covered.\n");
        var posted = await editor.PostFilesAsync("/manage/import?handler=Files", [("files", "corn.md", md)]);
        posted.StatusCode.ShouldBe(HttpStatusCode.Redirect, await posted.TextAsync());
        var reviewUrl = posted.Headers.Location!.ToString();
        var review = WebUtility.HtmlDecode(await editor.GetStringAsync(reviewUrl, Ct));
        review.ShouldNotContain("This import found 2 recipes");
        review.ShouldContain("Corn Bake (Double Batch) looked like a 2× batch of this recipe");
        review.ShouldContain("Double Batch (from import, 2×):");
        review.ShouldContain("Import it as its own recipe");

        var id = ReviewId().Match(reviewUrl).Groups[1].Value;
        var split = await editor.PostFormAsync($"/manage/import/{id}/review?handler=Split", ("index", "0"));
        split.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var second = WebUtility.HtmlDecode(await editor.GetStringAsync(split.Headers.Location!.ToString(), Ct));
        second.ShouldContain("Corn Bake (Double Batch)");
        second.ShouldContain("This import found 2 recipes:");

        var first = WebUtility.HtmlDecode(await editor.GetStringAsync(reviewUrl, Ct));
        first.ShouldNotContain("Double Batch (from import");
        first.ShouldNotContain("Import it as its own recipe");

        (await editor.PostFormAsync($"/manage/import/{id}/review?handler=Split", ("index", "0"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
