using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.E2E.Tests;

/// <summary>
/// FR-SCL-01, AC-30, ADR 0014 in a browser: the toolbar scales in place (htmx swap), the URL follows so the view can
/// be shared or printed, and the toolbar fits a phone screen (UX-01).
/// </summary>
[Trait("Req", "FR-SCL-01")]
[Trait("Req", "AC-30")]
[Trait("Req", "UX-13")]
public sealed class FR_SCL_01_ScaleToolbarTests(BrowserHost host) : IClassFixture<BrowserHost>
{
    [Fact]
    public async Task Reader_scales_a_recipe_with_the_toolbar_and_the_url_follows()
    {
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RecipeService>().CreateAsync(new RecipeEdit
            {
                Title = "Toolbar Pancakes",
                Servings = 4m,
                IngredientsText = "2 cups all-purpose flour\n3 eggs\n1 1/2 cups milk\n1 pinch salt",
                StepsText = "Whisk.\nCook on a 375°F griddle.",
                WantsPublish = true,
            }, "seed", TestContext.Current.CancellationToken);
        }

        await using var context = await host.NewContextAsync(width: 390, height: 844);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/recipes/toolbar-pancakes");
        (await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth")).ShouldBeTrue();

        await page.GetByRole(AriaRole.Button, new() { Name = "2x", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/recipes/toolbar-pancakes?units=as-written&scale=2");
        await Assertions.Expect(page.Locator(".ingredients li").First).ToContainTextAsync("4 cups all-purpose flour");
        await Assertions.Expect(page.Locator(".scaled-note")).ToContainTextAsync("serves 8");

        // UX-13: units are under "More scaling options"; the collapsed toolbar shows the quick presets only.
        await Assertions.Expect(page.Locator("#units")).ToBeHiddenAsync();
        await page.Locator(".scale-more > summary").ClickAsync();
        // The units list submits on change (recipe.js); the scale is kept.
        await page.Locator("#units").SelectOptionAsync("metric");
        await page.WaitForURLAsync("**scale=2&units=metric");
        await Assertions.Expect(page.Locator(".steps li").Nth(1)).ToContainTextAsync("190°C");
        await Assertions.Expect(page.Locator(".ingredients li").Nth(2)).ToContainTextAsync("710 ml milk");
        await Assertions.Expect(page.Locator("#units")).ToBeVisibleAsync(); // stays open across the in-place update

        // Owner feedback: closed on every page load; the summary says metric is in use.
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("#units")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator(".scale-more .more-active")).ToHaveTextAsync("Units: Metric");

        // Back to as written.
        await page.GetByRole(AriaRole.Link, new() { Name = "Show as written" }).ClickAsync();
        await Assertions.Expect(page.Locator(".ingredients li").First).ToContainTextAsync("2 cups all-purpose flour");
    }
}
