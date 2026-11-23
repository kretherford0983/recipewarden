using System.IO.Compression;
using System.Net;
using System.Text;

namespace RecipeWarden.Web.Tests;

/// <summary>
/// FR-EXP-01/02/03, FR-VIEW-02, AC-12, AC-23, AC-24, AC-25, ADR 0031: print, cook mode and export follow the reading
/// view's query; readers never reach drafts; bulk export is open and limited like search.
/// </summary>
[Trait("Req", "FR-EXP-01")]
public sealed class FR_EXP_01_PrintCookExportPageTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string> WebRecipeAsync(HttpClient editor, string title, string ingredients, string steps = "Mix.\nBake 25 minutes.")
    {
        var response = await editor.CreateRecipeAsync(title, ingredients, steps, "publish", null,
            ("Input.SourceType", "Web"), ("Input.SourceName", "Example Kitchen"), ("Input.SourceUrl", "https://example.com/" + title.ToLowerInvariant().Replace(' ', '-')),
            ("Input.Servings", "4"));
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return response.Headers.Location!.ToString()["/recipes/".Length..];
    }

    [Fact]
    [Trait("Req", "AC-24")]
    [Trait("Req", "AC-12")]
    public async Task Print_shows_the_scaled_amounts_weights_and_source_without_navigation()
    {
        using var editor = await factory.SignInAsync("print@example.com");
        var slug = await WebRecipeAsync(editor, "Print Cake", "1 cup all-purpose flour\n2 eggs");

        using var anon = factory.CreateHttpsClient();
        var html = await anon.GetStringAsync($"/recipes/{slug}/print?scale=2&units=weight", Ct);
        html.ShouldNotContain("primary-nav");
        html.ShouldNotContain("site-header");
        html.ShouldNotContain("/manage/");
        html.ShouldContain("Serves 8 (scaled 2x)");
        html.ShouldContain(" g all-purpose flour"); // weight view
        html.ShouldContain("4 eggs");
        html.ShouldContain("Source: Example Kitchen");
        html.ShouldContain(">https://example.com/print-cake</a>");
        html.ShouldContain("print.css");
        html.IndexOf("Ingredients", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("Steps", StringComparison.Ordinal));

        var reading = await anon.GetStringAsync($"/recipes/{slug}?scale=2&units=weight", Ct);
        reading.ShouldContain($"href=\"/recipes/{slug}/print?scale=2&amp;units=weight\"");
    }

    [Fact]
    [Trait("Req", "AC-23")]
    public async Task Cook_mode_has_ticks_timers_and_no_edit_controls()
    {
        using var editor = await factory.SignInAsync("cook@example.com");
        var slug = await WebRecipeAsync(editor, "Cook Bread", "3 cups flour", "Knead 10 minutes.\nBake 30 to 35 minutes.");

        using var anon = factory.CreateHttpsClient();
        var html = await anon.GetStringAsync($"/recipes/{slug}/cook", Ct);
        html.ShouldNotContain("/manage/");
        html.ShouldNotContain("method=\"post\"");
        html.ShouldContain("class=\"cook-tick\" data-tick=\"i0\"");
        html.ShouldContain("data-tick=\"s1\"");
        html.ShouldContain("data-seconds=\"600\"");
        html.ShouldContain("data-seconds=\"1800\" aria-label=\"Start a 30:00 timer\">30 to 35 minutes ⏱</button>");
        html.ShouldContain("cook.js");
        (await editor.GetStringAsync($"/recipes/{slug}/cook", Ct)).ShouldNotContain("/manage/recipes/");
    }

    [Fact]
    [Trait("Req", "AC-25")]
    public async Task Export_md_and_zip_follow_the_view_and_hold_the_source()
    {
        using var editor = await factory.SignInAsync("export@example.com");
        var slug = await WebRecipeAsync(editor, "Export Stew", "1 lb beef\n2 carrots");

        using var anon = factory.CreateHttpsClient();
        var md = await anon.GetAsync($"/recipes/{slug}/export.md?scale=2", Ct);
        md.StatusCode.ShouldBe(HttpStatusCode.OK);
        md.Content.Headers.ContentType!.MediaType.ShouldBe("text/markdown");
        md.Content.Headers.ContentDisposition!.FileName.ShouldBe(slug + ".md");
        var text = await md.Content.ReadAsStringAsync(Ct);
        text.ShouldContain("scale: 2");
        text.ShouldContain("- 4 carrots");
        text.ShouldContain("[Example Kitchen](https://example.com/export-stew)");

        var zip = await anon.GetAsync($"/recipes/{slug}/export.zip", Ct);
        zip.Content.Headers.ContentType!.MediaType.ShouldBe("application/zip");
        using var archive = new ZipArchive(await zip.Content.ReadAsStreamAsync(Ct));
        var entry = archive.Entries.ShouldHaveSingleItem();
        entry.FullName.ShouldBe(slug + ".md");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        (await reader.ReadToEndAsync(Ct)).ShouldNotContain("scale:");

        (await anon.GetAsync($"/recipes/{slug}/export.pdf", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anon.GetAsync("/recipes/no-such-recipe/export.md", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Req", "FR-EXP-03")]
    public async Task Readers_cannot_print_or_export_drafts_and_bulk_export_leaves_them_out()
    {
        using var editor = await factory.SignInAsync("bulk@example.com");
        var draft = await editor.CreateRecipeAsync("Secret Draft Pie", "1 crust", "Bake.", "draft");
        draft.StatusCode.ShouldBe(HttpStatusCode.Redirect); // drafts go back to the editor
        const string draftSlug = "secret-draft-pie";
        (await factory.RecipeIdAsync(draftSlug)).ShouldBeGreaterThan(0);
        var published = await editor.AddRecipeAsync("Bulk Published Pie");

        using var anon = factory.CreateHttpsClient();
        foreach (var path in new[] { "/print", "/cook", "/export.md", "/export.zip" })
        {
            (await anon.GetAsync($"/recipes/{draftSlug}{path}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
        }

        (await editor.GetAsync($"/recipes/{draftSlug}/export.md", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        static async Task<List<string>> Names(HttpResponseMessage response)
        {
            using var archive = new ZipArchive(await response.Content.ReadAsStreamAsync(Ct));
            return archive.Entries.Select(e => e.FullName).ToList();
        }

        var readerZip = await Names(await anon.GetAsync("/recipes/export.zip", Ct));
        readerZip.ShouldContain($"{published}/{published}.md");
        readerZip.ShouldNotContain(n => n.StartsWith(draftSlug, StringComparison.Ordinal));
        (await Names(await editor.GetAsync("/recipes/export.zip", Ct))).ShouldContain(n => n.StartsWith(draftSlug, StringComparison.Ordinal));

        await factory.SeedSamplesAsync();
        var collection = await Names(await anon.GetAsync($"/collections/{WebTestHelpers.SampleCollectionSlug}/export.zip", Ct));
        collection.ShouldContain($"{WebTestHelpers.SampleRecipeSlug}/{WebTestHelpers.SampleRecipeSlug}.md");
        collection.ShouldContain($"{WebTestHelpers.SampleRecipeSlug}/images/finish.webp");
        (await anon.GetAsync("/collections/no-such/export.zip", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_old_slug_redirects_to_the_same_view()
    {
        using var editor = await factory.SignInAsync("rename@example.com");
        var slug = await editor.AddRecipeAsync("Old Name Soup");
        var id = await factory.RecipeIdAsync(slug);
        var form = await editor.GetStringAsync($"/manage/recipes/{id}/edit", Ct);
        var version = System.Text.RegularExpressions.Regex.Match(form, "name=\"Input.Version\" value=\"(\\d+)\"").Groups[1].Value;
        (await editor.PostFormAsync($"/manage/recipes/{id}/edit", ("Input.Title", "New Name Soup"), ("Input.Ingredients", "1 cup flour"),
            ("Input.Steps", "Mix."), ("Input.SourceType", "Manual"), ("Input.Version", version), ("Action", "publish")))
            .StatusCode.ShouldBe(HttpStatusCode.Redirect);

        using var anon = factory.CreateHttpsClient();
        var moved = await anon.GetAsync($"/recipes/{slug}/print?scale=2", Ct);
        moved.StatusCode.ShouldBe(HttpStatusCode.MovedPermanently);
        moved.Headers.Location!.OriginalString.ShouldBe("/recipes/new-name-soup/print?scale=2");
    }
}
