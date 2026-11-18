using System.Net;
using System.Text.RegularExpressions;

namespace RecipeWarden.Web.Tests;

/// <summary>
/// FR-UI-01, UX-03, AC-61 (server part): the theme is rendered into &lt;html data-theme&gt; before first paint,
/// from the cookie for anonymous visitors and from the profile for signed-in users.
/// </summary>
[Trait("Req", "FR-UI-01")]
[Trait("Req", "UX-03")]
[Trait("Req", "AC-61")]
public sealed partial class FR_UI_01_ThemeTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static string DataTheme(string html) => HtmlTheme().Match(html).Groups[1].Value;

    private async Task<string> GetHomeAsync(string? cookie)
    {
        using var client = factory.CreateHttpsClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"rw-theme={cookie}");
        }

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(null, "system")]
    [InlineData("dark", "dark")]
    [InlineData("light", "light")]
    [InlineData("system", "system")]
    [InlineData("%3Cscript%3E", "system")]
    public async Task Anonymous_theme_comes_from_the_cookie(string? cookie, string expected) =>
        DataTheme(await GetHomeAsync(cookie)).ShouldBe(expected);

    [Fact]
    public async Task Layout_has_a_theme_toggle_and_loads_the_external_theme_script()
    {
        var html = await GetHomeAsync(null);
        html.ShouldContain("id=\"theme-toggle\"");
        html.ShouldContain("src=\"/js/theme.js");
    }

    [Fact]
    public async Task Anonymous_pages_do_not_expose_an_antiforgery_meta_tag()
    {
        var html = await GetHomeAsync(null);
        html.ShouldNotContain("rw-csrf");
    }

    [Fact]
    public async Task Signed_in_theme_is_saved_to_the_profile_and_wins_over_the_cookie()
    {
        using var client = await factory.SignInAsync("theme-user@example.com");
        var token = await RecipeWardenFactory.GetAntiforgeryTokenAsync(client, "/");

        using var save = new HttpRequestMessage(HttpMethod.Post, "/account/theme")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["theme"] = "dark" }),
        };
        save.Headers.Add("RequestVerificationToken", token);
        (await client.SendAsync(save, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var page = new HttpRequestMessage(HttpMethod.Get, "/");
        page.Headers.Add("Cookie", "rw-theme=light");
        var html = await (await client.SendAsync(page, TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        DataTheme(html).ShouldBe("dark");
    }

    [Fact]
    public async Task Profile_theme_is_copied_to_the_cookie_at_sign_in()
    {
        using (var first = await factory.SignInAsync("theme-cookie@example.com"))
        {
            var token = await RecipeWardenFactory.GetAntiforgeryTokenAsync(first, "/");
            using var save = new HttpRequestMessage(HttpMethod.Post, "/account/theme")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["theme"] = "light" }),
            };
            save.Headers.Add("RequestVerificationToken", token);
            await first.SendAsync(save, TestContext.Current.CancellationToken);
        }

        using var client = factory.CreateHttpsClient();
        var response = await RecipeWardenFactory.PostLoginAsync(client, "theme-cookie@example.com", RecipeWardenFactory.Password);
        response.Headers.GetValues("Set-Cookie").ShouldContain(c => c.StartsWith("rw-theme=light", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_theme_value_is_rejected()
    {
        using var client = await factory.SignInAsync("theme-bad@example.com");
        var token = await RecipeWardenFactory.GetAntiforgeryTokenAsync(client, "/");
        using var save = new HttpRequestMessage(HttpMethod.Post, "/account/theme")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["theme"] = "purple" }),
        };
        save.Headers.Add("RequestVerificationToken", token);

        (await client.SendAsync(save, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Req", "SEC-30")]
    [Trait("Req", "AC-75")]
    public async Task Theme_post_without_antiforgery_token_is_rejected()
    {
        using var client = await factory.SignInAsync("theme-csrf@example.com");
        var response = await client.PostAsync("/account/theme",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["theme"] = "dark" }), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [GeneratedRegex("<html[^>]*data-theme=\"([^\"]*)\"")]
    private static partial Regex HtmlTheme();
}
