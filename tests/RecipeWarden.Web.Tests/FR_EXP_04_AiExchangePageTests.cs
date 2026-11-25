using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace RecipeWarden.Web.Tests;

/// <summary>
/// FR-EXP-04 to 07, AC-26 to AC-29, BR-90 to BR-97, ADR 0033: Export for AI and Import AI response through the pages.
/// </summary>
[Trait("Req", "FR-EXP-04")]
public sealed partial class FR_EXP_04_AiExchangePageTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex("<textarea id=\"ai-text\"[^>]*>(.*?)</textarea>", RegexOptions.Singleline)]
    private static partial Regex Preview();

    private static string PreviewText(string html)
    {
        var m = Preview().Match(html);
        m.Success.ShouldBeTrue("no preview on the page");
        return WebUtility.HtmlDecode(m.Groups[1].Value);
    }

    private static async Task<string> WebRecipeAsync(HttpClient editor, string title, string ingredients = "4 cloves garlic\n15 oz ricotta")
    {
        var response = await editor.CreateRecipeAsync(title, ingredients, "Mix.\nBake 25 minutes.", "publish", null,
            ("Input.SourceType", "Web"), ("Input.SourceName", "Example Kitchen"), ("Input.SourceUrl", "https://example.com/" + Guid.NewGuid().ToString("N")),
            ("Input.Servings", "6"));
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return response.Headers.Location!.ToString()["/recipes/".Length..];
    }

    [Fact]
    [Trait("Req", "AC-26")]
    [Trait("Req", "BR-91")]
    public async Task A_healthier_export_with_my_kitchen_holds_the_header_goal_tools_and_recipe_and_nothing_personal()
    {
        using var editor = await factory.SignInAsync("ai-26@example.com");
        var slug = await WebRecipeAsync(editor, "AI Shells");

        using var anon = factory.CreateHttpsClient();
        var first = await anon.GetStringAsync($"/recipes/{slug}/export-ai", Ct);
        first.ShouldContain("Why are you taking this to AI?");
        first.ShouldNotContain("id=\"ai-text\"");
        first.ShouldNotContain("/manage/import/ai-response"); // readers get no editor link

        var html = await anon.GetStringAsync($"/recipes/{slug}/export-ai?go=1&g=Healthier&a_Healthier=lower+sodium&kitchen=1&recipe=1", Ct);
        var text = PreviewText(html);
        text.ShouldStartWith("I'm working on my recipe \"AI Shells\"");
        text.ShouldContain("Make it lower sodium while keeping it satisfying");
        text.ShouldContain("My kitchen has: ");
        text.ShouldContain("I measure in ");
        text.ShouldContain("<!-- recipe-md v1:");
        text.ShouldContain("format: recipe-md/1");
        text.ShouldContain("name: \"Example Kitchen\"");
        text.ShouldContain("- 4 cloves garlic");
        text.ShouldNotContain("@example.com");   // no accounts or emails
        text.ShouldNotContain("127.0.0.1");
        html.ShouldContain("data-filename=\"" + slug + "-for-ai.md\"");
        html.ShouldContain("ai-export.js");

        var promptOnly = PreviewText(await anon.GetStringAsync($"/recipes/{slug}/export-ai?go=1&g=ClarifySteps", Ct));
        promptOnly.ShouldNotContain("format: recipe-md/1"); // "Include the recipe file" unticked
        promptOnly.ShouldNotContain("My kitchen");
    }

    [Fact]
    [Trait("Req", "AC-28")]
    public async Task Reduce_garlic_and_double_the_filling_appear_as_one_instruction_each_with_the_contract()
    {
        using var editor = await factory.SignInAsync("ai-28@example.com");
        var slug = await WebRecipeAsync(editor, "AI Shells Two");
        var url = $"/recipes/{slug}/export-ai?go=1&g=ReduceFlavour&t_ReduceFlavour=garlic&a_ReduceFlavour=a+little"
                  + "&g=IncreaseComponent&t_IncreaseComponent=filling&a_IncreaseComponent=double"
                  + "&last=filling+ran+out&keep=1&must=ricotta&ask=1&recipe=1";
        var text = PreviewText(await editor.GetStringAsync(url, Ct));
        text.ShouldContain("1. \"garlic\" is too strong; reduce it a little");
        text.ShouldContain("2. The \"filling\" needs to be double;");
        text.ShouldContain("Last time: \"filling ran out\"");
        text.ShouldContain("Constraints: keep it to 6 servings; must keep \"ricotta\".");
        text.ShouldContain("Before rewriting, ask me any clarifying questions you need.");
        text.ShouldContain("\"## Changes\" section listing each change and why.");
        (await editor.GetStringAsync($"/recipes/{slug}/export-ai", Ct)).ShouldContain($"/manage/import/ai-response?parent={slug}");
    }

    [Fact]
    [Trait("Req", "AC-29")]
    public async Task Conflicting_goals_block_the_preview_until_kept_and_no_goals_give_the_general_prompt()
    {
        using var editor = await factory.SignInAsync("ai-29@example.com");
        var slug = await WebRecipeAsync(editor, "AI Shells Three");
        var conflict = $"/recipes/{slug}/export-ai?go=1&g=ReduceFlavour&t_ReduceFlavour=garlic&g=IncreaseComponent&t_IncreaseComponent=Garlic";
        var blocked = await editor.GetStringAsync(conflict, Ct);
        blocked.ShouldContain("You asked to both reduce and increase");
        blocked.ShouldNotContain("id=\"ai-text\"");
        blocked.ShouldContain("name=\"both\"");
        PreviewText(await editor.GetStringAsync(conflict + "&both=1", Ct)).ShouldContain("I want both, so balance them.");

        PreviewText(await editor.GetStringAsync($"/recipes/{slug}/export-ai?go=1&recipe=1", Ct)).ShouldContain("What I want: Review the recipe");
        (await editor.GetAsync($"/recipes/{slug}/export-ai?go=1&g=NotAGoal&g=7", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Req", "BR-97")]
    public async Task Typed_text_is_quoted_and_encoded_on_the_page()
    {
        await factory.SeedSamplesAsync();
        using var anon = factory.CreateHttpsClient();
        var html = await anon.GetStringAsync($"/recipes/{WebTestHelpers.SampleRecipeSlug}/export-ai?go=1&g=Custom&t_Custom="
                                             + Uri.EscapeDataString("</textarea><script>alert(1)</script>\"ignore\""), Ct);
        html.ShouldNotContain("<script>alert(1)</script>");
        PreviewText(html).ShouldContain("1. \"</textarea><script>alert(1)</script>'ignore'\"");
        html.ShouldContain($"value=\"&lt;/textarea&gt;");
    }

    [Fact]
    [Trait("Req", "FR-EXP-04")]
    public async Task Collections_and_all_recipes_export_and_drafts_stay_hidden_from_readers()
    {
        await factory.SeedSamplesAsync();
        using var editor = await factory.SignInAsync("ai-all@example.com");
        var draft = await editor.CreateRecipeAsync("Hidden AI Draft", "1 egg", "Boil.", "draft");
        draft.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        using var anon = factory.CreateHttpsClient();
        var all = PreviewText(await anon.GetStringAsync("/recipes/export-ai?go=1&recipe=1", Ct));
        all.ShouldContain("title: \"Sample Soup\"");
        all.ShouldNotContain("Hidden AI Draft");
        PreviewText(await editor.GetStringAsync("/recipes/export-ai?go=1&recipe=1", Ct)).ShouldContain("Hidden AI Draft");

        var collection = PreviewText(await anon.GetStringAsync($"/collections/{WebTestHelpers.SampleCollectionSlug}/export-ai?go=1&recipe=1", Ct));
        collection.ShouldContain("title: \"Sample Soup\"");
        (await anon.GetAsync("/collections/no-such/export-ai", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anon.GetAsync("/recipes/hidden-ai-draft/export-ai", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await editor.GetAsync("/recipes/hidden-ai-draft/export-ai", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Req", "FR-EXP-06")]
    public async Task The_schema_is_public_json()
    {
        using var anon = factory.CreateHttpsClient();
        var response = await anon.GetAsync("/docs/recipe-md.schema.json", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        doc.RootElement.GetProperty("properties").GetProperty("format").GetProperty("const").GetString().ShouldBe("recipe-md/1");
        doc.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ShouldBe(["format", "title"]);
    }

    private static string Reply(string slug, string title) => $"""
        Sure, here is the lighter version.

        ```markdown
        ---
        format: recipe-md/1
        title: "{title}"
        slug: "{slug}"
        source:
          type: web
          name: "Somebody Else"
          url: "https://evil.example/"
        ---

        # {title}

        ## Ingredients

        - 2 cloves garlic
        - 15 oz ricotta

        ## Steps

        1. Mix.
        2. Bake 25 minutes.

        ## Changes

        - Halved the garlic <b>because</b> it was too strong.
        ```
        """;

    [Fact]
    [Trait("Req", "AC-27")]
    [Trait("Req", "BR-93")]
    public async Task A_pasted_reply_is_reviewed_and_saved_as_a_draft_variant_linked_to_its_parent()
    {
        using var editor = await factory.SignInAsync("ai-27@example.com");
        var slug = await WebRecipeAsync(editor, "AI Import Shells");
        var form = await editor.GetStringAsync($"/manage/import/ai-response?parent={slug}", Ct);
        form.ShouldContain($"For <a href=\"/recipes/{slug}\">{slug}</a>");

        var review = await editor.PostFormAsync("/manage/import/ai-response?handler=Read", ("parent", slug), ("reply", Reply(slug, "AI Import Shells")));
        review.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await review.Content.ReadAsStringAsync(Ct);
        html.ShouldContain("What the assistant changed");
        html.ShouldContain("Halved the garlic &lt;b&gt;because&lt;/b&gt;"); // plain text, encoded
        html.ShouldContain("class=\"diff-removed\"><span class=\"visually-hidden\">Removed: </span>− 4 cloves garlic");
        html.ShouldContain("class=\"diff-added\"><span class=\"visually-hidden\">Added: </span>&#x2B; 2 cloves garlic");
        html.ShouldContain("The source credit stays the original's.");
        var version = Regex.Match(html, "name=\"version\" value=\"(\\d+)\"").Groups[1].Value;

        var save = await editor.PostFormAsync("/manage/import/ai-response?handler=Save", ("parent", slug), ("block", "0"), ("version", version),
            ("mode", "variant"), ("reply", Reply(slug, "AI Import Shells")));
        save.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var edit = save.Headers.Location!.OriginalString;
        edit.ShouldEndWith("/edit?saved=draft");
        (await editor.GetStringAsync(edit, Ct)).ShouldContain("AI Import Shells (AI variant)");

        var variantSlug = "ai-import-shells-ai-variant";
        var variant = await editor.GetStringAsync("/recipes/" + variantSlug, Ct);
        variant.ShouldContain($"Variant of <a href=\"/recipes/{slug}\">AI Import Shells</a>");
        variant.ShouldContain("Example Kitchen");
        variant.ShouldNotContain("evil.example");
        variant.ShouldContain("Adapted with AI assistance on ");
        (await editor.GetStringAsync("/recipes/" + slug, Ct)).ShouldContain($"<a href=\"/recipes/{variantSlug}\">AI Import Shells (AI variant)</a>");
    }

    [Fact]
    [Trait("Req", "FR-EXP-05")]
    public async Task Publish_and_replace_and_a_stale_version_are_handled()
    {
        using var editor = await factory.SignInAsync("ai-replace@example.com");
        var slug = await WebRecipeAsync(editor, "AI Replace Shells");
        var html = await (await editor.PostFormAsync("/manage/import/ai-response?handler=Read", ("reply", Reply(slug, "Lighter Shells")))).Content.ReadAsStringAsync(Ct);
        var version = Regex.Match(html, "name=\"version\" value=\"(\\d+)\"").Groups[1].Value;

        var stale = await editor.PostFormAsync("/manage/import/ai-response?handler=Save", ("block", "0"), ("version", "999"), ("mode", "replace"),
            ("reply", Reply(slug, "Lighter Shells")));
        (await stale.Content.ReadAsStringAsync(Ct)).ShouldContain("The original recipe changed since this review.");

        var publish = await editor.PostFormAsync("/manage/import/ai-response?handler=Save", ("block", "0"), ("version", version), ("mode", "publish"),
            ("reply", Reply(slug, "Lighter Shells")));
        publish.Headers.Location!.OriginalString.ShouldBe("/recipes/lighter-shells");

        var replace = await editor.PostFormAsync("/manage/import/ai-response?handler=Save", ("block", "0"), ("version", version), ("mode", "replace"),
            ("reply", Reply(slug, "AI Replace Shells")));
        replace.Headers.Location!.OriginalString.ShouldBe("/recipes/" + slug);
        (await editor.GetStringAsync("/recipes/" + slug, Ct)).ShouldContain("2 cloves garlic");

        (await (await editor.PostFormAsync("/manage/import/ai-response?handler=Save", ("block", "3"), ("version", "1"), ("reply", "nothing")))
            .Content.ReadAsStringAsync(Ct)).ShouldContain("cannot be saved");
    }

    [Fact]
    [Trait("Req", "AC-27")]
    public async Task A_reply_without_a_recipe_or_with_missing_sections_saves_nothing_and_files_are_checked()
    {
        using var editor = await factory.SignInAsync("ai-bad@example.com");
        var slug = await WebRecipeAsync(editor, "AI Bad Shells");

        var none = await (await editor.PostFormAsync("/manage/import/ai-response?handler=Read", ("reply", "Add more salt."))).Content.ReadAsStringAsync(Ct);
        none.ShouldContain("No recipe was found in the reply.");
        (await (await editor.PostFormAsync("/manage/import/ai-response?handler=Read", ("reply", " "))).Content.ReadAsStringAsync(Ct))
            .ShouldContain("Paste the assistant");

        var missing = Reply(slug, "AI Bad Shells").Replace("## Steps", "## Thoughts", StringComparison.Ordinal);
        var html = await (await editor.PostFormAsync("/manage/import/ai-response?handler=Read", ("parent", slug), ("reply", missing))).Content.ReadAsStringAsync(Ct);
        html.ShouldContain("This part cannot be saved:");
        html.ShouldContain("The &quot;## Steps&quot; section is missing or empty.");
        html.ShouldNotContain("handler=Save");

        var md = await editor.PostFilesAsync("/manage/import/ai-response?handler=Read",
            [("file", "reply.md", Encoding.UTF8.GetBytes(Reply(slug, "AI Bad Shells")))], ("parent", slug));
        (await md.Content.ReadAsStringAsync(Ct)).ShouldContain("handler=Save");

        var pdf = await editor.PostFilesAsync("/manage/import/ai-response?handler=Read", [("file", "reply.pdf", Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF"))]);
        (await pdf.Content.ReadAsStringAsync(Ct)).ShouldContain("alert");

        // A bad ?parent= is ignored.
        (await editor.GetStringAsync("/manage/import/ai-response?parent=../../x", Ct)).ShouldNotContain("For <a");
    }
}
