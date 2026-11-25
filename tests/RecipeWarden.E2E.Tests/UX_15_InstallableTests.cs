using Microsoft.Playwright;

namespace RecipeWarden.E2E.Tests;

/// <summary>
/// UX-15, AC-134, ADR 0058: Chrome itself reports the site as installable (no installability errors), and the tab
/// icon is the one the page links to. Uses Chrome's own check through the DevTools protocol.
/// </summary>
[Trait("Req", "UX-15")]
[Trait("Req", "AC-134")]
public sealed class UX_15_InstallableTests(BrowserHost host) : IClassFixture<BrowserHost>
{
    [Fact]
    public async Task Chrome_finds_no_reason_the_site_cannot_be_installed()
    {
        await using var context = await host.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var cdp = await context.NewCDPSessionAsync(page);
        var manifest = await cdp.SendAsync("Page.getAppManifest");
        manifest!.Value.GetProperty("url").GetString().ShouldEndWith("/manifest.webmanifest");
        manifest.Value.GetProperty("errors").GetArrayLength().ShouldBe(0, manifest.Value.ToString());

        // A test browser context is incognito, where Chrome never installs apps; every other reason would be ours.
        var result = await cdp.SendAsync("Page.getInstallabilityErrors");
        var errors = result!.Value.GetProperty("installabilityErrors").EnumerateArray()
            .Select(e => e.GetProperty("errorId").GetString()).Where(id => id != "in-incognito").ToList();
        errors.ShouldBeEmpty();

        var icon = await page.EvaluateAsync<string>("() => document.querySelector('link[rel=icon][type=\"image/svg+xml\"]').href");
        (await page.APIRequest.GetAsync(icon)).Status.ShouldBe(200);
    }
}
