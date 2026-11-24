using System.Net;

namespace RecipeWarden.Web.Tests;

/// <summary>
/// FR-NUT-07, BR-116, AC-102: a fake matcher that names one food the local data does not have and one it does
/// (FDC 2709267, FNDDS "Pomegranate, raw"); dismissals; Editor only.
/// </summary>
[Trait("Req", "FR-NUT-07")]
[Trait("Req", "BR-116")]
public sealed class FR_NUT_07_SuggestionFallbackTests : IClassFixture<FR_NUT_07_SuggestionFallbackTests.Factory>
{
    private readonly RecipeWardenFactory factory;

    public FR_NUT_07_SuggestionFallbackTests(Factory f) => factory = f.Host;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed class Factory : IDisposable
    {
        public Factory()
        {
            Host = new RecipeWardenFactory();
            PythonWebTestKit.UseFakeWorker(Host, """
                def food(name):
                    if "nowhere" in name:
                        return [{"fdc_id": 99999999, "data_type": "survey_fndds_food", "description": "Imaginary food", "confidence": 1.0}]
                    if "unsure" in name:
                        return [{"fdc_id": 2709267, "data_type": "survey_fndds_food", "description": "Pomegranate, raw", "confidence": 0.5}]
                    return [{"fdc_id": 2709267, "data_type": "survey_fndds_food", "description": "Pomegranate, raw", "confidence": 0.9}]
                answer({"suggestions": [{"name": n, "fdc": food(n)} for n in req["names"]]})
                """);
        }

        public RecipeWardenFactory Host { get; }

        public void Dispose() => Host.Dispose();
    }

    [Fact]
    [Trait("Req", "AC-102")]
    public async Task A_food_that_is_not_in_the_local_data_or_an_unsure_one_is_never_shown()
    {
        using var editor = await factory.SignInAsync("nut07f-102@example.com");
        await editor.AddRecipeAsync("Mystery Stew", "1 cup nowhere spice\n2 Tbsp unsure seeds");

        var page = WebUtility.HtmlDecode(await (await editor.PostFormAsync("/manage/nutrition?handler=SuggestMatches")).TextAsync());

        page.ShouldContain("0 suggested matches to check");
        page.ShouldNotContain("Imaginary food");
        page.ShouldNotContain("Pomegranate, raw");
    }

    [Fact]
    public async Task A_dismissed_suggestion_is_not_shown_again()
    {
        using var editor = await factory.SignInAsync("nut07f-dismiss@example.com");
        await editor.AddRecipeAsync("Ruby Bowl", "1 cup ruby kernels");
        var page = WebUtility.HtmlDecode(await (await editor.PostFormAsync("/manage/nutrition?handler=SuggestMatches")).TextAsync());
        page.ShouldContain("Pomegranate, raw");
        page.ShouldContain("FNDDS · 90% sure");

        var dismissed = await editor.PostFormAsync("/manage/nutrition?handler=Dismiss", ("name", "ruby kernels"));
        dismissed.Headers.Location!.OriginalString.ShouldBe("/manage/nutrition?done=dismissed");
        var after = WebUtility.HtmlDecode(await editor.GetStringAsync("/manage/nutrition?done=dismissed", Ct));
        after.ShouldContain("<td>ruby kernels</td>");
        after.ShouldNotContain("Pomegranate, raw");
        await editor.PostFormAsync("/manage/nutrition?handler=SuggestMatches");
        WebUtility.HtmlDecode(await editor.GetStringAsync("/manage/nutrition", Ct)).ShouldNotContain("Pomegranate, raw");
    }

    [Fact]
    [Trait("Req", "SEC-10")]
    public async Task Only_editors_can_ask_for_or_dismiss_suggestions()
    {
        using var viewer = await factory.SignInAsync("nut07f-viewer@example.com", role: null);
        (await viewer.PostFormAsync("/manage/nutrition?handler=SuggestMatches", [], tokenPage: "/")).StatusCode
            .ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Redirect);
        using var editor = await factory.SignInAsync("nut07f-csrf@example.com");
        (await editor.PostAsync("/manage/nutrition?handler=SuggestMatches", new FormUrlEncodedContent([]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}

/// <summary>AC-103, BR-119: with no worker (the default test host) the report works as in v1 and says suggestions are unavailable.</summary>
[Trait("Req", "AC-103")]
public sealed class FR_NUT_07_NoWorkerTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    [Fact]
    public async Task Without_a_worker_the_report_still_works_and_says_why_nothing_was_suggested()
    {
        using var editor = await factory.SignInAsync("nut07n-editor@example.com");
        await editor.AddRecipeAsync("Plain Seeds", "1 cup pomegranate seeds");

        var response = await editor.PostFormAsync("/manage/nutrition?handler=SuggestMatches");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = WebUtility.HtmlDecode(await response.TextAsync());
        page.ShouldContain(Pages.Manage.NutritionModel.SuggestionsUnavailable);
        page.ShouldContain("<td>pomegranate seeds</td>");
    }
}
