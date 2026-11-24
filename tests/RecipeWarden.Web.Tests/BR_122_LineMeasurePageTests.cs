using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace RecipeWarden.Web.Tests;

/// <summary>
/// BR-122, AC-115, AC-116: scale suggestions line by line, and the per-line measure choice in the editor. Each test
/// has its own factory because the kitchen is shared by the whole site.
/// </summary>
[Trait("Req", "FR-TOOL-05")]
[Trait("Req", "BR-122")]
public sealed partial class BR_122_LineMeasurePageTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex("name=\"Input.Version\" value=\"(\\d+)\"")]
    private static partial Regex VersionField();

    private static async Task<(RecipeWardenFactory Factory, HttpClient Editor, string Slug, int Id, string Key)> SetupAsync(string user)
    {
        var factory = new RecipeWardenFactory();
        var editor = await factory.SignInAsync(user);
        var response = await editor.CreateRecipeAsync("Pick Loaf", "2 cups all-purpose flour\n1 cup whole milk",
            extra: [("Input.IsPrecision", "true")]);
        var slug = response.Headers.Location!.ToString()["/recipes/".Length..];
        await editor.PostFormAsync("/manage/kitchen?handler=Preset", ("key", "scale"));
        var key = "weight:scale-" + (await factory.ToolIdAsync("Digital scale")).ToString(CultureInfo.InvariantCulture);
        return (factory, editor, slug, await factory.RecipeIdAsync(slug), key);
    }

    [Fact]
    [Trait("Req", "AC-115")]
    public async Task Saving_only_the_ticked_line_weighs_it_and_keeps_the_rest_as_written()
    {
        var (factory, editor, slug, id, key) = await SetupAsync("pick-save@example.com");
        await using var _ = factory;
        using var __ = editor;

        var banner = await factory.ReadAsync($"/recipes/{slug}", editor);
        banner.ShouldContain("name=\"line\" value=\"0\" checked");
        banner.ShouldContain("name=\"line\" value=\"1\" checked");
        banner.ShouldContain("Untick any you would rather measure with spoons and cups.");

        (await editor.PostFormAsync($"/manage/recipes/{id}/suggestions?handler=Apply", ("key", key), ("picked", "true"), ("line", "1")))
            .StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var html = await factory.ReadAsync($"/recipes/{slug}");
        html.ShouldContain("2 cups all-purpose flour");
        html.ShouldNotContain("250 g all-purpose flour");
        html.ShouldContain("244 g whole milk");
        html.ShouldNotContain("by weight?");

        var form = WebUtility.HtmlDecode(await editor.GetStringAsync($"/manage/recipes/{id}/edit", Ct));
        form.ShouldContain("<option value=\"AsWritten\" selected=\"selected\">As written</option>");
        form.ShouldContain("<option value=\"Weight\" selected=\"selected\">By weight</option>");
    }

    [Fact]
    [Trait("Req", "AC-116")]
    public async Task Show_me_weighs_only_the_ticked_lines_and_saves_nothing()
    {
        var (factory, editor, slug, _, _) = await SetupAsync("pick-show@example.com");
        await using var _f = factory;
        using var _e = editor;
        var seen = await factory.SeenVersionAsync(slug);

        var reader = await factory.ReadAsync($"/recipes/{slug}");
        reader.ShouldContain("name=\"w\" value=\"1\" checked");
        reader.ShouldNotContain("handler=Apply");

        var shown = await factory.ReadAsync($"/recipes/{slug}?w=1");
        shown.ShouldContain("244 g whole milk");
        shown.ShouldContain("2 cups all-purpose flour");
        shown.ShouldContain("Showing weights for this view.");
        (await factory.ReadAsync($"/recipes/{slug}?weights=0")).ShouldContain("250 g all-purpose flour");
        (await factory.ReadAsync($"/recipes/{slug}?w=1")).ShouldContain("/print?w=1");
        (await factory.SeenVersionAsync(slug)).ShouldBe(seen);
    }

    [Fact]
    [Trait("Req", "BR-122")]
    public async Task The_editor_sets_a_line_by_weight_and_the_choice_follows_the_line_across_saves()
    {
        var (factory, editor, slug, id, _) = await SetupAsync("pick-editor@example.com");
        await using var _f = factory;
        using var _e = editor;

        async Task SaveAsync(string ingredients, params (string, string)[] measures)
        {
            var page = await editor.GetStringAsync($"/manage/recipes/{id}/edit", Ct);
            page.ShouldContain("Measure by weight or as written");
            var fields = new List<(string, string)>
            {
                ("Input.Title", "Pick Loaf"), ("Input.Ingredients", ingredients), ("Input.Steps", "Mix."), ("Input.IsPrecision", "true"),
                ("Input.SourceType", "Manual"), ("Input.Version", VersionField().Match(page).Groups[1].Value), ("Action", "publish"),
            };
            fields.AddRange(measures);
            (await editor.PostFormAsync($"/manage/recipes/{id}/edit", [.. fields])).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        await SaveAsync("2 cups all-purpose flour\n1 cup whole milk",
            ("Input.MeasureLine", "2 cups all-purpose flour"), ("Input.MeasureChoice", "Weight"),
            ("Input.MeasureLine", "1 cup whole milk"), ("Input.MeasureChoice", "Bogus"));
        var html = await factory.ReadAsync($"/recipes/{slug}");
        html.ShouldContain("250 g all-purpose flour");
        html.ShouldContain("1 cup whole milk");

        await SaveAsync("2 cups all-purpose flour\n1 cup whole milk\n1 tsp salt");
        (await factory.ReadAsync($"/recipes/{slug}")).ShouldContain("250 g all-purpose flour");

        await SaveAsync("3 cups all-purpose flour\n1 cup whole milk");
        (await factory.ReadAsync($"/recipes/{slug}")).ShouldContain("3 cups all-purpose flour");
    }
}
