using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.E2E.Tests;

/// <summary>W1 on a phone (UX-01, UX-11, AC-23 reading part): find a recipe and read it with no edit controls.</summary>
[Trait("Req", "FR-VIEW-01")]
[Trait("Req", "AC-23")]
[Trait("Req", "UX-01")]
public sealed class W1_PhoneReadingTests(BrowserHost host) : IClassFixture<BrowserHost>
{
    [Fact]
    public async Task Anonymous_reader_on_a_phone_finds_and_reads_a_recipe()
    {
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RecipeService>().CreateAsync(new RecipeEdit
            {
                Title = "Green Curry",
                Description = "Weeknight **favourite**.",
                IngredientsText = "1 (13.5 oz) can coconut milk\n2 tbsp green curry paste\n1 lb chicken thighs, sliced",
                StepsText = "Fry the paste.\nAdd coconut milk.\nSimmer the chicken.",
                Tags = ["Thai"],
                WantsPublish = true,
            }, "seed", TestContext.Current.CancellationToken);
        }

        await using var context = await host.NewContextAsync(width: 390, height: 844);
        var page = await context.NewPageAsync();

        await page.GotoAsync("/");
        await page.Locator("#site-search").FillAsync("curry");
        await page.Locator("#site-search").PressAsync("Enter");
        await page.WaitForURLAsync("**/search?q=curry");
        await page.GetByRole(AriaRole.Link, new() { Name = "Green Curry" }).ClickAsync();
        await page.WaitForURLAsync("**/recipes/green-curry");

        await Assertions.Expect(page.Locator(".ingredients li")).ToHaveCountAsync(3);
        await Assertions.Expect(page.Locator(".steps li")).ToHaveCountAsync(3);
        await Assertions.Expect(page.Locator("strong", new() { HasText = "favourite" })).ToBeVisibleAsync();

        // UX-11 / AC-23: no edit controls for readers.
        await Assertions.Expect(page.Locator("a[href^='/manage']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Edit" })).ToHaveCountAsync(0);

        // UX-01: one column under 768 px (ingredients above steps), and nothing wider than the screen.
        var ingredientsTop = await page.Locator(".ingredients").EvaluateAsync<double>("e => e.getBoundingClientRect().top");
        var stepsTop = await page.Locator(".steps").EvaluateAsync<double>("e => e.getBoundingClientRect().top");
        stepsTop.ShouldBeGreaterThan(ingredientsTop);
        (await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth")).ShouldBeTrue();
    }
}
