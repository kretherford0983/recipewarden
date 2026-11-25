using System.Net;
using System.Text;
using AngleSharp.Html.Parser;
using RecipeWarden.Core.Security;

namespace RecipeWarden.Web.Tests;

/// <summary>
/// UX-13, ADR 0025, AC-144 (owner testing 2026-11-25): the review page's editor is a normal card, the Import options
/// box only shows when it holds an option, and Save draft, Publish and Discard this import share the sticky bar.
/// </summary>
[Trait("Req", "UX-13")]
[Trait("Req", "AC-144")]
public sealed class AC_144_ReviewActionBarTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(HttpClient Editor, string ReviewUrl)> ImportMarkdownAsync(string user)
    {
        var editor = await factory.SignInAsync(user, AppRoles.Editor);
        var md = Encoding.UTF8.GetBytes("# Bar Soup\n## Ingredients\n- 1 cup broth\n## Steps\n1. Heat.");
        var response = await editor.PostFilesAsync("/manage/import?handler=Files", [("files", "soup.md", md)]);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect, await response.TextAsync());
        return (editor, response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task A_markdown_review_has_a_card_form_no_empty_options_and_discard_in_the_bar()
    {
        var (editor, review) = await ImportMarkdownAsync("bar-1@example.com");
        using var _ = editor;
        var doc = await new HtmlParser().ParseDocumentAsync(await editor.GetStringAsync(review, Ct), Ct);

        var form = doc.QuerySelector("form#recipe-editor").ShouldNotBeNull();
        form.ParentElement!.ClassList.ShouldContain("content", "the editor is a card: a direct child of .content");
        form.ClassList.ShouldNotContain("ai-parsed");
        doc.QuerySelector("fieldset.review-options").ShouldBeNull("a Markdown file has no import options");

        var bar = form.QuerySelector(".sticky-actions").ShouldNotBeNull();
        var buttons = bar.QuerySelectorAll("button").ToList();
        buttons.Select(b => b.TextContent.Trim()).ShouldBe(["Save draft", "Publish", "Discard this import"]);
        var discard = buttons[2];
        discard.GetAttribute("form").ShouldBe("discard-import");
        discard.HasAttribute("formnovalidate").ShouldBeTrue();

        var discardForm = doc.QuerySelector("form#discard-import").ShouldNotBeNull();
        discardForm.GetAttribute("method").ShouldBe("post");
        discardForm.GetAttribute("action").ShouldBe(review + "?handler=Discard");
        discardForm.QuerySelector("input[name=__RequestVerificationToken]").ShouldNotBeNull();
        discardForm.QuerySelectorAll("button").ShouldBeEmpty();
        doc.QuerySelectorAll("form.discard").ShouldBeEmpty("no second Discard button below the form");
    }

    [Fact]
    public async Task The_options_box_shows_when_there_is_an_option()
    {
        const string url = "http://kitchen.example/bar-options";
        factory.Web.Html(url, """
            <html><head><script type="application/ld+json">{"@type":"Recipe","name":"Option Soup","image":"http://cdn.example/bar.jpg",
              "recipeIngredient":["1 cup broth"],"recipeInstructions":["Heat."]}</script></head><body></body></html>
            """);
        using var editor = await factory.SignInAsync("bar-2@example.com", AppRoles.Editor);
        var response = await editor.PostFormAsync("/manage/import?handler=Url", ("Url", url));
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect, await response.TextAsync());
        var doc = await new HtmlParser().ParseDocumentAsync(await editor.GetStringAsync(response.Headers.Location!.ToString(), Ct), Ct);

        var options = doc.QuerySelector("fieldset.review-options").ShouldNotBeNull();
        options.QuerySelector("input#CopyImage").ShouldNotBeNull();
    }

    [Fact]
    [Trait("Req", "SEC-30")]
    public async Task Discard_from_the_bar_posts_the_discard_form()
    {
        var (editor, review) = await ImportMarkdownAsync("bar-3@example.com");
        using var _ = editor;
        var discarded = await editor.PostFormAsync(review + "?handler=Discard");
        discarded.Headers.Location!.ToString().ShouldBe("/manage/import");
    }
}
