using System.Net;
using System.Text.Json;
using RecipeWarden.Web.Operations;

namespace RecipeWarden.Web.Tests;

/// <summary>UX-15, AC-134: every page has a tab icon, the icons are served, and the manifest makes the site installable.</summary>
[Trait("Req", "UX-15")]
public sealed class UX_15_AppIconTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [Trait("Req", "AC-134")]
    [InlineData("/")]
    [InlineData("/account/login")]
    [InlineData("/recipes")]
    [InlineData("/error?code=404")]
    public async Task Pages_link_the_icons_and_the_manifest(string path)
    {
        using var client = factory.CreateHttpsClient();
        var html = await (await client.GetAsync(path, Ct)).Content.ReadAsStringAsync(Ct);
        html.ShouldContain("<link rel=\"icon\" href=\"/favicon.ico\"");
        html.ShouldContain("<link rel=\"icon\" href=\"/favicon.svg\" type=\"image/svg+xml\">");
        html.ShouldContain("<link rel=\"apple-touch-icon\" href=\"/apple-touch-icon.png\">");
        html.ShouldContain("<link rel=\"manifest\" href=\"/manifest.webmanifest\">");
        html.ShouldContain("<meta name=\"theme-color\" content=\"#a33f19\"");
    }

    [Theory]
    [Trait("Req", "AC-134")]
    [InlineData("/favicon.ico", "image/x-icon")]
    [InlineData("/favicon.svg", "image/svg+xml")]
    [InlineData("/apple-touch-icon.png", "image/png")]
    [InlineData("/icons/icon-192.png", "image/png")]
    [InlineData("/icons/icon-512.png", "image/png")]
    [InlineData("/icons/maskable-192.png", "image/png")]
    [InlineData("/icons/maskable-512.png", "image/png")]
    public async Task The_icons_are_served_to_anyone(string path, string type)
    {
        using var client = factory.CreateHttpsClient();
        var response = await client.GetAsync(path, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe(type);
        (await response.Content.ReadAsByteArrayAsync(Ct)).Length.ShouldBeGreaterThan(200);
    }

    [Fact]
    [Trait("Req", "AC-134")]
    public async Task The_manifest_is_named_from_the_site_and_lists_its_icons()
    {
        using var client = factory.CreateHttpsClient();
        var response = await client.GetAsync("/manifest.webmanifest", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/manifest+json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var root = json.RootElement;
        root.GetProperty("name").GetString().ShouldBe("RecipeWarden");
        root.GetProperty("display").GetString().ShouldBe("standalone");
        root.GetProperty("start_url").GetString().ShouldBe("/");
        foreach (var icon in root.GetProperty("icons").EnumerateArray())
        {
            (await client.GetAsync(icon.GetProperty("src").GetString(), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task The_print_view_and_the_maintenance_page_have_an_icon()
    {
        using var editor = await factory.SignInAsync("icons-print@example.com");
        var slug = await editor.AddRecipeAsync("Icon Toast", "2 slices bread");
        (await editor.GetStringAsync($"/recipes/{slug}/print", Ct)).ShouldContain("<link rel=\"icon\" href=\"/favicon.svg\"");
        MaintenanceMiddleware.Page.ShouldContain("<link rel=\"icon\" href=\"data:image/svg+xml,");
    }
}
