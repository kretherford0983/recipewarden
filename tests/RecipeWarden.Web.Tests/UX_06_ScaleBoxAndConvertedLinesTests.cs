using System.Net;

namespace RecipeWarden.Web.Tests;

/// <summary>
/// UX-06, UX-13 (owner feedback 2026-11-23, after the dev build): the scale box's options are always closed when the
/// page loads, and the summary says which of them is in use; a converted line says in words what was written (a dot
/// looked like a second bullet, and a tooltip cannot be read on a phone).
/// </summary>
[Trait("Req", "UX-06")]
[Trait("Req", "UX-13")]
public sealed class UX_06_ScaleBoxAndConvertedLinesTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> RecipeAsync(string title)
    {
        using var editor = await factory.SignInAsync("ux06-editor@example.com");
        var response = await editor.CreateRecipeAsync(title, "24 oz. chicken broth (low sodium)\n1 yellow onion (diced)", "Simmer.",
            extra: [("Input.Servings", "4")]);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect, await response.TextAsync());
        return response.Headers.Location!.ToString();
    }

    private async Task<string> GetAsync(string url, string? cookie = null)
    {
        using var client = factory.CreateHttpsClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        var response = await client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, url);
        return WebUtility.HtmlDecode(await response.TextAsync());
    }

    [Theory]
    [InlineData("?units=metric", null, "Units: Metric")]
    [InlineData("", "rw-units=us", "Units: US")]
    [InlineData("?scale=1.5", null, "1.5x")]
    [InlineData("?servings=6", null, "Serves 6")]
    public async Task The_options_stay_closed_on_load_and_the_summary_names_what_is_in_use(string query, string? cookie, string active)
    {
        var url = await RecipeAsync("Closed Box " + Guid.NewGuid().ToString("N")[..6]);
        var html = await GetAsync(url + query, cookie);

        html.ShouldContain("<details class=\"scale-more\">");
        html.ShouldNotContain("<details class=\"scale-more\" open");
        html.ShouldContain($"<span class=\"more-active\">{active}</span>");
    }

    [Fact]
    public async Task As_written_the_summary_names_nothing()
    {
        var html = await GetAsync(await RecipeAsync("Plain Box"));
        html.ShouldContain("<details class=\"scale-more\">");
        html.ShouldNotContain("class=\"more-active\"");
    }

    [Fact]
    [Trait("Req", "BR-115")]
    public async Task A_converted_line_says_what_was_written_in_words()
    {
        var url = await RecipeAsync("Converted Broth");

        // BR-115, ADR 0049: ounces of broth are fluid ounces, never pounds or grams.
        var us = await GetAsync(url + "?units=us");
        us.ShouldContain("24 fl oz chicken broth");
        us.ShouldNotContain("lb chicken broth");
        us.ShouldContain("<span class=\"written\">Written: 24 oz. chicken broth (low sodium)</span>");

        var metric = await GetAsync(url + "?units=metric");
        metric.ShouldContain("710 ml chicken broth");

        var asWritten = await GetAsync(url);
        asWritten.ShouldContain("24 oz chicken broth");
        asWritten.ShouldNotContain("class=\"written\"");
    }
}
