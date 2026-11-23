using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.E2E.Tests;

/// <summary>W10 (FR-EXP-04 to 07, AC-27, AC-28, BR-97): improve a recipe with an AI assistant, in a browser.</summary>
[Trait("Req", "W10")]
public sealed class W10_AiExchangeTests(BrowserHost host) : IClassFixture<BrowserHost>
{
    [Fact]
    [Trait("Req", "AC-28")]
    [Trait("Req", "AC-27")]
    public async Task W10_export_with_goals_copy_the_edited_preview_and_bring_the_reply_back_as_a_variant()
    {
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RecipeService>().CreateAsync(new RecipeEdit
            {
                Title = "W10 Shells", IngredientsText = "4 cloves garlic\n15 oz ricotta", StepsText = "Mix.\nBake 25 minutes.",
                Servings = 6m, WantsPublish = true,
            }, "seed", TestContext.Current.CancellationToken);
        }

        await host.CreateUserAsync("w10@example.com", AppRoles.Editor);
        await using var context = await host.NewContextAsync();
        await context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        var page = await context.NewPageAsync();
        await BrowserHost.SignInAsync(page, "w10@example.com");

        // 1. Export for AI with two goals.
        await page.GotoAsync("/recipes/w10-shells");
        await page.GetByRole(AriaRole.Link, new() { Name = "Export for AI" }).ClickAsync();
        await page.GetByLabel("Reduce a flavour or ingredient", new() { Exact = true }).CheckAsync();
        await page.Locator("#t-ReduceFlavour").FillAsync("garlic");
        await page.Locator("#a-ReduceFlavour").SelectOptionAsync("a little");
        await page.GetByLabel("Increase a component", new() { Exact = true }).CheckAsync();
        await page.Locator("#t-IncreaseComponent").FillAsync("filling");
        await page.Locator("#a-IncreaseComponent").SelectOptionAsync("double");
        await page.GetByRole(AriaRole.Button, new() { Name = "Build the prompt" }).ClickAsync();

        var preview = page.Locator("#ai-text");
        await Assertions.Expect(preview).ToHaveValueAsync(new System.Text.RegularExpressions.Regex("\"garlic\" is too strong; reduce it a little"));

        // BR-97: what is copied is exactly what is shown, edits included.
        await preview.FillAsync((await preview.InputValueAsync()).Replace("reduce it a little", "reduce it by half", StringComparison.Ordinal));
        await page.GetByRole(AriaRole.Button, new() { Name = "Copy", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".ai-status")).ToHaveTextAsync("Copied.");
        var copied = await page.EvaluateAsync<string>("() => navigator.clipboard.readText()");
        copied.ShouldBe(await preview.InputValueAsync());
        copied.ShouldContain("reduce it by half");

        // 2–3. The assistant's reply comes back and becomes a variant.
        var reply = """
            Here is the revised recipe:

            ```markdown
            ---
            format: recipe-md/1
            title: "W10 Shells"
            slug: "w10-shells"
            servings: 6
            ---

            # W10 Shells

            ## Ingredients

            - 2 cloves garlic
            - 30 oz ricotta

            ## Steps

            1. Mix.
            2. Bake 25 minutes.

            ## Changes

            - Halved the garlic and doubled the ricotta.
            ```
            """;
        await page.GetByRole(AriaRole.Link, new() { Name = "Import AI response" }).ClickAsync();
        await page.Locator("#ai-reply").FillAsync(reply);
        await page.GetByRole(AriaRole.Button, new() { Name = "Review" }).ClickAsync();
        await Assertions.Expect(page.Locator(".ai-changes")).ToContainTextAsync("Halved the garlic");
        await Assertions.Expect(page.Locator(".diff-added").First).ToContainTextAsync("2 cloves garlic");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save and publish variant" }).ClickAsync();
        await page.WaitForURLAsync("**/recipes/w10-shells-ai-variant");
        await Assertions.Expect(page.Locator(".variant-of")).ToContainTextAsync("Variant of W10 Shells");
        await Assertions.Expect(page.Locator(".ingredients")).ToContainTextAsync("30 oz ricotta");
    }
}
