using Microsoft.Playwright;
using RecipeWarden.Core.Security;

namespace RecipeWarden.E2E.Tests;

/// <summary>W2 (FR-REC-02, AC-01): an Editor adds a recipe by hand in a real browser.</summary>
[Trait("Req", "FR-REC-02")]
[Trait("Req", "AC-01")]
public sealed class W2_AddRecipeTests(BrowserHost host) : IClassFixture<BrowserHost>
{
    [Fact]
    [Trait("Req", "FR-REC-03")]
    public async Task Editor_types_a_recipe_sees_live_chips_reorders_steps_and_publishes()
    {
        await host.CreateUserAsync("w2@example.com", AppRoles.Editor);
        await using var context = await host.NewContextAsync();
        var page = await context.NewPageAsync();
        await BrowserHost.SignInAsync(page, "w2@example.com");

        await page.GotoAsync("/manage/recipes/new");
        await page.GetByLabel("Title").FillAsync("Lemon Rice");
        await page.GetByLabel("Servings").FillAsync("6-8");          // ADR 0010: ranges, as recipes print them
        await page.GetByLabel("Cook time").FillAsync("1 hr 15 min");
        await page.Locator("#Input_Ingredients").FillAsync("1 ½ cups basmati rice, rinsed\n2 cups water\n1 lemon, zested");

        // W2 step 3: each line is parsed live and shown as chips.
        await Assertions.Expect(page.Locator("#ingredient-preview .chip.qty").First).ToHaveTextAsync("1½");
        await Assertions.Expect(page.Locator("#ingredient-preview .chip.name").First).ToHaveTextAsync("basmati rice");
        await Assertions.Expect(page.Locator("#ingredient-preview .chip.prep").First).ToHaveTextAsync("rinsed");

        await page.Locator("#Input_Steps").FillAsync("1. Rinse.\n2. Simmer.\n3. Zest the lemon.");

        // FR-REC-02: reorder with the keyboard-accessible buttons (drag uses the same code path).
        await page.Locator("textarea[data-reorder=steps] + .reorder-toggle").ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Move down: 2. Simmer." }).ClickAsync();
        await Assertions.Expect(page.Locator("#Input_Steps")).ToHaveValueAsync("1. Rinse.\n3. Zest the lemon.\n2. Simmer.");

        await page.GetByRole(AriaRole.Button, new() { Name = "Publish" }).ClickAsync();
        await page.WaitForURLAsync("**/recipes/lemon-rice");

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Lemon Rice", Level = 1 })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".ingredients li").First).ToHaveTextAsync("1½ cups basmati rice, rinsed");
        await Assertions.Expect(page.Locator(".facts dd").First).ToHaveTextAsync("6–8");
        await Assertions.Expect(page.Locator(".facts")).ToContainTextAsync("1 hr 15 min");
        await Assertions.Expect(page.Locator(".steps li")).ToHaveTextAsync(["Rinse.", "Zest the lemon.", "Simmer."]);

        // AC-01: found by search straight away.
        await page.GotoAsync("/search?q=lemon");
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Lemon Rice" })).ToBeVisibleAsync();
    }

    [Fact]
    [Trait("Req", "AC-02")]
    public async Task Saving_without_steps_keeps_a_draft_and_says_why()
    {
        await host.CreateUserAsync("w2b@example.com", AppRoles.Editor);
        await using var context = await host.NewContextAsync();
        var page = await context.NewPageAsync();
        await BrowserHost.SignInAsync(page, "w2b@example.com");

        await page.GotoAsync("/manage/recipes/new");
        await page.GetByLabel("Title").FillAsync("Half Done");
        await page.Locator("#Input_Ingredients").FillAsync("1 egg");
        await page.GetByRole(AriaRole.Button, new() { Name = "Publish" }).ClickAsync();

        await page.WaitForURLAsync("**/edit?saved=draft");
        await Assertions.Expect(page.GetByText("Add at least one step to publish")).ToBeVisibleAsync();
    }
}
