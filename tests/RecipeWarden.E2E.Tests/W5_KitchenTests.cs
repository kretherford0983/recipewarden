using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.E2E.Tests;

/// <summary>
/// W5 in a browser (FR-TOOL-01/04/05, AC-41, ADR 0017): an editor adds a scale in My Kitchen, a recipe offers
/// weights, "Show me" shows them for that view, and Dismiss hides the banner.
/// </summary>
[Trait("Req", "FR-TOOL-04")]
[Trait("Req", "AC-41")]
public sealed class W5_KitchenTests(BrowserHost host) : IClassFixture<BrowserHost>
{
    [Fact]
    public async Task Editor_adds_a_scale_and_acts_on_the_suggestion()
    {
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RecipeService>().CreateAsync(new RecipeEdit
            {
                Title = "W5 Sponge",
                IsPrecision = true,
                IngredientsText = "2 cups all-purpose flour\n4 eggs",
                StepsText = "Whisk.\nBake.",
                WantsPublish = true,
            }, "seed", TestContext.Current.CancellationToken);
        }

        await host.CreateUserAsync("w5@example.com", AppRoles.Editor);
        await using var context = await host.NewContextAsync();
        var page = await context.NewPageAsync();
        await BrowserHost.SignInAsync(page, "w5@example.com");

        await page.GotoAsync("/manage/kitchen");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add: Digital scale (1 g / 0.05 oz)" }).ClickAsync();
        await Assertions.Expect(page.Locator(".notice")).ToContainTextAsync("Added.");
        await Assertions.Expect(page.Locator("table.kitchen")).ToContainTextAsync("Digital scale");

        await page.GotoAsync("/recipes/w5-sponge");
        var banner = page.Locator(".suggestion-banner");
        await Assertions.Expect(banner).ToContainTextAsync("Show 1 ingredient by weight?");
        await Assertions.Expect(page.Locator(".ingredients li").First).ToContainTextAsync("2 cups all-purpose flour");

        // BR-122: Show me is now a button that weighs the ticked lines (recipe.js puts them in the URL).
        await banner.GetByRole(AriaRole.Button, new() { Name = "Show me" }).ClickAsync();
        await page.WaitForURLAsync("**weights=0");
        await Assertions.Expect(page.Locator(".ingredients li").First).ToContainTextAsync("250 g all-purpose flour");

        await page.GotoAsync("/recipes/w5-sponge");
        await page.Locator(".suggestion-banner").GetByRole(AriaRole.Button, new() { Name = "Dismiss" }).ClickAsync();
        await page.WaitForURLAsync("**/recipes/w5-sponge");
        await Assertions.Expect(page.Locator(".suggestion-banner")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator(".ingredients li").First).ToContainTextAsync("2 cups all-purpose flour");
    }
}
