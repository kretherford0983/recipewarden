using Microsoft.Playwright;

namespace RecipeWarden.E2E.Tests;

/// <summary>
/// AC-61, FR-UI-01, UX-03: an anonymous visitor picks dark mode; a week later the first paint is already dark. The
/// return visit runs with JavaScript off, which proves the server, not a script, set the theme before painting.
/// </summary>
[Trait("Req", "AC-61")]
[Trait("Req", "FR-UI-01")]
[Trait("Req", "UX-03")]
public sealed class AC_61_ThemeFirstPaintTests(BrowserHost host) : IClassFixture<BrowserHost>
{
    [Fact]
    public async Task Dark_mode_chosen_by_an_anonymous_visitor_is_painted_first_on_return()
    {
        await using var first = await host.NewContextAsync();
        var page = await first.NewPageAsync();
        await page.GotoAsync("/");
        var toggle = page.Locator("#theme-toggle");
        for (var i = 0; i < 3 && await page.EvaluateAsync<string>("() => document.documentElement.dataset.theme") != "dark"; i++)
        {
            await toggle.ClickAsync();
        }

        (await page.EvaluateAsync<string>("() => document.documentElement.dataset.theme")).ShouldBe("dark");
        var cookie = (await first.CookiesAsync()).Single(c => c.Name == "rw-theme");
        cookie.Value.ShouldBe("dark");
        DateTimeOffset.FromUnixTimeSeconds((long)cookie.Expires).ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddDays(7)); // still there in a week

        var returning = await host.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = host.BaseUrl,
            JavaScriptEnabled = false,
            ColorScheme = ColorScheme.Light, // the system prefers light; the saved choice must win
            ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Forwarded-Proto"] = "https" },
        });
        await using (returning)
        {
            await returning.AddCookiesAsync([new Cookie { Name = "rw-theme", Value = "dark", Url = host.BaseUrl, Secure = true }]);
            var again = await returning.NewPageAsync();
            var response = await again.GotoAsync("/", new() { WaitUntil = WaitUntilState.Commit });
            (await response!.TextAsync()).ShouldContain("data-theme=\"dark\"");
            await again.WaitForLoadStateAsync(LoadState.Load);
            var background = await again.EvaluateAsync<string>("() => getComputedStyle(document.body).backgroundColor");
            background.ShouldBe("rgb(23, 21, 19)"); // --bg in the dark theme
        }
    }
}
