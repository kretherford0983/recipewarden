using System.Net;

namespace RecipeWarden.Web.Tests;

/// <summary>SEC-21, AC-22, FR-SRCH-01/02 over HTTP: hostile search input returns a normal page, safely echoed.</summary>
[Trait("Req", "SEC-21")]
[Trait("Req", "AC-22")]
[Trait("Req", "FR-SRCH-01")]
public sealed class SEC_21_SearchPageTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("%22%20OR%201%3D1%20--")]
    [InlineData("NEAR(")]
    [InlineData("title%3Achicken")]
    [InlineData("%27%3B%20DROP%20TABLE%20Recipes%3B%20--")]
    [InlineData("%00")]
    [InlineData("%ED%A0%80")]   // a lone surrogate encoded as UTF-8 (invalid)
    [InlineData("chicken&sort=Title%20DESC%3B%20DROP%20TABLE%20Recipes&time=-1&rating=99&page=-5&tag=..%2Fx&collection=%3Cx%3E")]
    public async Task Hostile_query_strings_return_a_normal_page(string query)
    {
        using var reader = factory.CreateHttpsClient();
        var response = await reader.GetAsync("/search?q=" + query, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.TextAsync();
        html.ShouldContain("<h1>Search</h1>");
        html.ShouldNotContain("SqliteException");
    }

    [Fact]
    public async Task The_query_is_echoed_encoded()
    {
        using var reader = factory.CreateHttpsClient();
        var html = await reader.GetStringAsync("/search?q=%3Cscript%3Ealert(1)%3C%2Fscript%3E", Ct);

        html.ShouldNotContain("<script>alert(1)</script>");
        html.ShouldContain("&lt;script&gt;alert(1)&lt;/script&gt;");
    }

    [Fact]
    [Trait("Req", "AC-21")]
    [Trait("Req", "FR-SRCH-02")]
    public async Task Results_rank_and_filter_and_pager_links_keep_the_query()
    {
        using var editor = await factory.SignInAsync("search-page@example.com");
        await editor.AddRecipeAsync("Chicken Lime Tacos", "1 lb chicken\n2 limes", "Grill.", "dinner");
        await editor.AddRecipeAsync("Lime Sorbet", "4 limes", "Freeze.", "dessert");

        using var reader = factory.CreateHttpsClient();
        var html = await reader.GetStringAsync("/search?q=chicken+lime", Ct);
        html.IndexOf("Chicken Lime Tacos", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("Lime Sorbet", StringComparison.Ordinal));
        html.ShouldContain("2 recipes");

        var filtered = await reader.GetStringAsync("/search?q=lime&tag=dessert", Ct);
        filtered.ShouldContain("Lime Sorbet");
        filtered.ShouldNotContain("Chicken Lime Tacos");

        var browse = await reader.GetStringAsync("/recipes?sort=title", Ct);
        browse.IndexOf("Chicken Lime Tacos", StringComparison.Ordinal).ShouldBeLessThan(browse.IndexOf("Lime Sorbet", StringComparison.Ordinal));
    }
}
