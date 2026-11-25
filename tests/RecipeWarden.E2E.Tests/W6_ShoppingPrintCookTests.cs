using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.E2E.Tests;

/// <summary>
/// W6 (FR-SHOP-01..03, AC-50, AC-51), print on one Letter page (AC-24) and cook mode on a phone (AC-23), in a browser.
/// </summary>
[Trait("Req", "W6")]
public sealed class W6_ShoppingPrintCookTests(BrowserHost host) : IClassFixture<BrowserHost>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> SeedAsync(RecipeEdit edit)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var saved = await scope.ServiceProvider.GetRequiredService<RecipeService>().CreateAsync(edit with { WantsPublish = true }, "seed", Ct);
        return saved.Slug!;
    }

    [Fact]
    [Trait("Req", "AC-50")]
    [Trait("Req", "AC-51")]
    public async Task W6_an_editor_builds_a_list_and_a_phone_ticks_it_off()
    {
        var cookies = await SeedAsync(new RecipeEdit { Title = "W6 Cookies", IngredientsText = "1 cup butter\n1 tsp salt", StepsText = "Mix." });
        var brownies = await SeedAsync(new RecipeEdit { Title = "W6 Brownies", IngredientsText = "4 Tbsp butter\n2 eggs", StepsText = "Bake." });
        await host.CreateUserAsync("w6@example.com", AppRoles.Editor);

        await using var desk = await host.NewContextAsync();
        var page = await desk.NewPageAsync();
        await BrowserHost.SignInAsync(page, "w6@example.com");
        await page.GotoAsync("/recipes/" + cookies);
        await page.Locator("#new-list-name").FillAsync("Saturday");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".notice")).ToContainTextAsync("Added to Saturday");

        await page.GotoAsync("/recipes/" + brownies);
        await page.Locator("#shopping-list").SelectOptionAsync(new SelectOptionValue { Label = "Saturday" });
        await page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Saturday" }).ClickAsync();
        var listUrl = page.Url;

        var butter = page.Locator(".shopping-items li", new() { HasText = "butter" });
        await Assertions.Expect(butter).ToContainTextAsync("1¼ cups");
        await Assertions.Expect(butter).ToContainTextAsync("(2½ sticks)");
        await Assertions.Expect(butter).ToContainTextAsync("for W6 Cookies, W6 Brownies");
        await Assertions.Expect(page.Locator(".pantry")).ToContainTextAsync("salt");

        // A phone, not signed in (D-10), ticks both items to buy; htmx swaps the list in place.
        await using var phoneContext = await host.NewContextAsync(width: 390, height: 844);
        var phone = await phoneContext.NewPageAsync();
        await phone.GotoAsync(listUrl);
        await Assertions.Expect(phone.GetByText("Add an item")).ToHaveCountAsync(0);
        await phone.GetByRole(AriaRole.Button, new() { Name = "Tick butter" }).ClickAsync();
        await Assertions.Expect(phone.GetByRole(AriaRole.Button, new() { Name = "Untick butter" })).ToBeVisibleAsync();
        phone.Url.ShouldBe(listUrl);
        await phone.GetByRole(AriaRole.Button, new() { Name = "Tick egg" }).ClickAsync();
        await Assertions.Expect(phone.Locator(".notice")).ToContainTextAsync("All done");

        // The desk sees the ticks (stored on the server) and can undo.
        await page.ReloadAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Untick butter" })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Untick egg" }).ClickAsync();
        await Assertions.Expect(page.Locator(".notice", new() { HasText = "All done" })).ToHaveCountAsync(0);
    }

    [Fact]
    [Trait("Req", "AC-24")]
    public async Task A_fifteen_ingredient_ten_step_recipe_prints_on_one_letter_page_at_2x_with_weights()
    {
        var ingredients = string.Join('\n',
            "2 cups all-purpose flour", "1 cup granulated sugar", "1/2 cup brown sugar", "1 cup butter, softened", "2 eggs",
            "1 tsp vanilla extract", "1 tsp baking soda", "1/2 tsp salt", "1 cup chocolate chips", "1/2 cup walnuts, chopped",
            "1/4 cup whole milk", "1 tbsp honey", "1/2 tsp ground cinnamon", "1/4 cup old-fashioned oats", "1/4 cup shredded coconut");
        var steps = string.Join('\n', Enumerable.Range(1, 10).Select(i =>
            $"For stage {i}, combine the ingredients, stir until just mixed, and scrape the bowl well before going on."));
        var slug = await SeedAsync(new RecipeEdit
        {
            Title = "W6 Print Cookies", IngredientsText = ingredients, StepsText = steps, Servings = 24m,
            SourceType = SourceType.Web, SourceName = "Example Kitchen", SourceUrl = "https://example.com/print-cookies",
        });

        await using var context = await host.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"/recipes/{slug}/print?scale=2&units=weight");
        await Assertions.Expect(page.Locator("main")).ToContainTextAsync("https://example.com/print-cookies");
        await Assertions.Expect(page.Locator("main")).ToContainTextAsync(" g all-purpose flour");
        await page.EmulateMediaAsync(new() { Media = Media.Print });
        await Assertions.Expect(page.Locator(".screen-only")).ToBeHiddenAsync();

        var pdf = await page.PdfAsync(new PagePdfOptions { Format = "Letter", PreferCSSPageSize = true });
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);
        document.NumberOfPages.ShouldBe(1);
        var text = string.Join(' ', document.GetPages().Select(p => p.Text));
        text.ShouldContain("Example Kitchen");
        text.ShouldContain("stage 10");
    }

    [Fact]
    [Trait("Req", "AC-23")]
    public async Task Cook_mode_on_a_phone_keeps_ticks_on_the_device_and_starts_timers()
    {
        var slug = await SeedAsync(new RecipeEdit { Title = "W6 Rice", IngredientsText = "1 cup rice\n2 cups water", StepsText = "Rinse.\nSimmer 18 minutes." });

        await using var context = await host.NewContextAsync(width: 390, height: 844);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"/recipes/{slug}");
        await page.GetByRole(AriaRole.Link, new() { Name = "Cook mode" }).ClickAsync();
        await page.WaitForURLAsync($"**/recipes/{slug}/cook");
        await Assertions.Expect(page.Locator(".wake-status")).Not.ToBeEmptyAsync();

        var rice = page.Locator(".cook-ingredients input.cook-tick").First;
        await rice.CheckAsync();
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator(".cook-ingredients input.cook-tick").First).ToBeCheckedAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Start a 18:00 timer" }).ClickAsync();
        await Assertions.Expect(page.Locator(".cook-timers .timer")).ToContainTextAsync("18 minutes");
        await Assertions.Expect(page.Locator(".cook-timers .timer strong")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("^1[78]:[0-5][0-9]$"));

        await page.GetByRole(AriaRole.Button, new() { Name = "Clear ticks" }).ClickAsync();
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator(".cook-ingredients input.cook-tick").First).Not.ToBeCheckedAsync();
    }
}
