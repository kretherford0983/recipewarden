using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.E2E.Tests;

/// <summary>
/// W7 (FR-BKP-01, 04, AC-80, AC-82) and W9 (FR-OPS-02, AC-98) in a browser: an Admin backs up, checks the archive,
/// types the confirmation word and restores; then opens Health and the move guide.
/// </summary>
[Trait("Req", "FR-BKP-04")]
[Trait("Req", "AC-82")]
[Trait("Req", "FR-OPS-02")]
public sealed class W7_W9_OperationsTests(BrowserHost host) : IClassFixture<BrowserHost>
{
    [Fact]
    public async Task Admin_backs_up_restores_and_checks_health()
    {
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RecipeService>().CreateAsync(new RecipeEdit
            {
                Title = "W7 Backed Up Bread",
                IngredientsText = "3 cups flour",
                StepsText = "Knead.",
                WantsPublish = true,
            }, "seed", TestContext.Current.CancellationToken);
        }

        await host.CreateUserAsync("w7@example.com", AppRoles.Admin);
        await using var context = await host.NewContextAsync();
        var page = await context.NewPageAsync();
        await BrowserHost.SignInAsync(page, "w7@example.com");

        await page.GotoAsync("/admin");
        await page.GetByRole(AriaRole.Link, new() { Name = "Backups" }).First.ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Back up now" }).ClickAsync();
        await Assertions.Expect(page.Locator(".notice")).ToContainTextAsync("Backup finished.");
        await Assertions.Expect(page.Locator("#hist-h + table")).ToContainTextAsync("Manual");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RecipeService>().CreateAsync(new RecipeEdit
            {
                Title = "W7 After The Backup",
                IngredientsText = "1 egg",
                StepsText = "Boil.",
                WantsPublish = true,
            }, "seed", TestContext.Current.CancellationToken);
        }

        await page.Locator("summary", new() { HasText = "Restore…" }).First.ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Check this archive" }).First.ClickAsync();
        await Assertions.Expect(page.Locator(".restore-report")).ToContainTextAsync("Ready to restore");
        await page.GetByLabel("Type RESTORE to confirm").FillAsync("RESTORE");
        await page.GetByRole(AriaRole.Button, new() { Name = "Restore now" }).ClickAsync();
        await Assertions.Expect(page.Locator(".notice")).ToContainTextAsync("Restore finished.");

        await page.GotoAsync("/recipes");
        await Assertions.Expect(page.Locator("main")).ToContainTextAsync("W7 Backed Up Bread");
        await Assertions.Expect(page.Locator("main")).Not.ToContainTextAsync("W7 After The Backup");

        await page.GotoAsync("/admin/health");
        await Assertions.Expect(page.Locator(".overall")).ToContainTextAsync("Overall:");
        await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Space by category");
        await page.Locator("#move-guide summary").ClickAsync();
        await Assertions.Expect(page.Locator("#move-guide")).ToContainTextAsync("--restore");
    }
}
