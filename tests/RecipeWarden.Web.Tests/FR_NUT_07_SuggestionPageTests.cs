using System.Net;
using System.Text.RegularExpressions;

namespace RecipeWarden.Web.Tests;

/// <summary>FR-NUT-07, AC-101, ADR 0051, ADR 0052: suggestions from the real bundled matcher, confirmed by an Editor.</summary>
[Trait("Req", "FR-NUT-07")]
public sealed partial class FR_NUT_07_SuggestionPageTests : IClassFixture<FR_NUT_07_SuggestionPageTests.Factory>
{
    private readonly RecipeWardenFactory factory;

    public FR_NUT_07_SuggestionPageTests(Factory f) => factory = f.Host;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed class Factory : IDisposable
    {
        public Factory()
        {
            Host = new RecipeWardenFactory();
            PythonWebTestKit.UseRealWorker(Host);
        }

        public RecipeWardenFactory Host { get; }

        public void Dispose() => Host.Dispose();
    }

    [GeneratedRegex("name=\"nutrientId\" value=\"(\\d+)\"")]
    private static partial Regex NutrientId();

    [Fact]
    [Trait("Req", "AC-101")]
    [Trait("Req", "BR-116")]
    public async Task An_unmatched_ingredient_gets_a_local_suggestion_and_using_it_maps_every_recipe()
    {
        using var editor = await factory.SignInAsync("nut07-editor@example.com");
        var slugs = new[]
        {
            await editor.AddRecipeAsync("Jewel Salad", "1 cup pomegranate seeds\n1 cup water"),
            await editor.AddRecipeAsync("Jewel Yogurt", "2 Tbsp pomegranate seeds\n1 cup water"),
        };
        (await editor.GetStringAsync("/recipes/" + slugs[0], Ct)).ShouldContain("50% of ingredients matched");
        var before = WebUtility.HtmlDecode(await editor.GetStringAsync("/manage/nutrition", Ct));
        before.ShouldContain("<td>pomegranate seeds</td>");
        before.ShouldContain("Suggest matches");

        var suggested = await editor.PostFormAsync("/manage/nutrition?handler=SuggestMatches");
        suggested.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = WebUtility.HtmlDecode(await suggested.TextAsync());
        page.ShouldContain("suggested match");
        page.ShouldContain("Pomegranate, raw");
        page.ShouldMatch(@"(SR Legacy|Foundation|FNDDS) · \d+% sure");

        // Nothing is mapped until the Editor chooses "Use" (BR-116).
        (await editor.GetStringAsync("/recipes/" + slugs[0], Ct)).ShouldContain("50% of ingredients matched");
        var row = page[page.IndexOf("Pomegranate, raw", StringComparison.Ordinal)..];
        var nutrientId = NutrientId().Match(row).Groups[1].Value;
        var used = await editor.PostFormAsync("/manage/nutrition?handler=Map", ("name", "pomegranate seeds"), ("nutrientId", nutrientId));
        used.Headers.Location!.OriginalString.ShouldBe("/manage/nutrition?done=mapped");
        foreach (var slug in slugs)
        {
            (await editor.GetStringAsync("/recipes/" + slug, Ct)).ShouldContain("100% of ingredients matched");
        }

        (await editor.GetStringAsync("/manage/nutrition", Ct)).ShouldNotContain("<td>pomegranate seeds</td>");
    }

    [Fact]
    public async Task Asking_again_does_not_ask_about_names_already_answered()
    {
        using var editor = await factory.SignInAsync("nut07-again@example.com");
        await editor.AddRecipeAsync("Seed Mix", "1 cup pomegranate arils");
        (await editor.PostFormAsync("/manage/nutrition?handler=SuggestMatches")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = WebUtility.HtmlDecode(await (await editor.PostFormAsync("/manage/nutrition?handler=SuggestMatches")).TextAsync());
        second.ShouldContain("already been looked at");
    }
}
