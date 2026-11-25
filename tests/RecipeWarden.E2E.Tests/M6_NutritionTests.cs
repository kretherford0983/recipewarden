using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.E2E.Tests;

/// <summary>FR-NUT-01, FR-ALG-01, BR-53, AC-55, AC-56 in a browser: a phone reader filters by allergen and reads the panel.</summary>
[Trait("Req", "FR-ALG-01")]
public sealed class M6_NutritionTests(BrowserHost host) : IClassFixture<BrowserHost>
{
    [Fact]
    [Trait("Req", "BR-53")]
    [Trait("Req", "AC-55")]
    public async Task A_phone_reader_filters_out_milk_and_sees_badges_and_nutrition()
    {
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var recipes = scope.ServiceProvider.GetRequiredService<RecipeService>();
            await recipes.CreateAsync(new RecipeEdit { Title = "M6 Cream Soup", IngredientsText = "1 cup whole milk\n2 Tbsp butter", StepsText = "Heat.", WantsPublish = true }, "seed", TestContext.Current.CancellationToken);
            await recipes.CreateAsync(new RecipeEdit { Title = "M6 Soy Noodles", IngredientsText = "8 oz spaghetti\n2 Tbsp soy sauce", StepsText = "Boil.", WantsPublish = true }, "seed", TestContext.Current.CancellationToken);
        }

        await using var context = await host.NewContextAsync(width: 390, height: 844);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/search?q=M6");
        await page.Locator(".free-of summary").ClickAsync();
        await page.GetByLabel("Milk", new() { Exact = true }).CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "M6 Soy Noodles" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "M6 Cream Soup" })).ToHaveCountAsync(0);

        await page.GetByRole(AriaRole.Link, new() { Name = "M6 Soy Noodles" }).ClickAsync();
        var badges = page.Locator("ul.allergens");
        await Assertions.Expect(badges).ToContainTextAsync("Wheat");
        await Assertions.Expect(badges).ToContainTextAsync("Soy");
        await Assertions.Expect(page.Locator(".nutrition-panel")).ToContainTextAsync("Calculated from the ingredients");
        await Assertions.Expect(page.Locator(".nutrition-panel")).ToContainTextAsync("estimates");
    }
}
