using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.E2E.Tests;

/// <summary>
/// AC-96, NFR-03, ADR 0045: the main pages in both themes have no problems of the kinds axe rates serious or critical
/// (contrast, names, labels, alt text, ARIA references, lists, nested controls, zoom), checked by a small in-page
/// script (a11y-check.js). Keyboard focus is checked to be visible.
/// </summary>
[Trait("Req", "AC-96")]
[Trait("Req", "NFR-03")]
public sealed class AC_96_AccessibilityTests(BrowserHost host) : IClassFixture<BrowserHost>
{
    private static readonly string Script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "a11y-check.js"));

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Problem(string Rule, string Target, string Detail);

    private async Task<int> SeedAsync()
    {
        await using var scope = host.Services.CreateAsyncScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<RecipeService>().CreateAsync(new RecipeEdit
        {
            Title = "Accessible Apple Crumble",
            Description = "Sharp apples, **buttery** crumble.",
            IngredientsText = "For the filling:\n6 apples, sliced\n2 tbsp sugar\nFor the topping:\n1 cup flour\n½ cup butter",
            StepsText = "Heat the oven to 375°F.\nToss the apples with sugar.\nRub the butter into the flour and scatter.\nBake 40 minutes.",
            Notes = "Good with custard.",
            Tags = ["Dessert", "Fall"],
            WantsPublish = true,
        }, "seed", TestContext.Current.CancellationToken);
        return outcome.Id;
    }

    private static async Task<List<string>> CheckAsync(IPage page, string path)
    {
        var failures = new List<string>();
        var response = await page.GotoAsync(path);
        if (response is null || response.Status != 200 || page.Url.Contains("/account/login", StringComparison.Ordinal) != path.StartsWith("/account/login", StringComparison.Ordinal))
        {
            failures.Add($"{path}: not shown ({response?.Status}, {page.Url})");
            return failures;
        }

        foreach (var theme in new[] { "light", "dark" })
        {
            await page.EvaluateAsync("t => document.documentElement.setAttribute('data-theme', t)", theme);
            var json = await page.EvaluateAsync<JsonElement>(Script);
            var problems = json.Deserialize<List<Problem>>(Json)!;
            failures.AddRange(problems.Select(p => $"{path} [{theme}] {p.Rule}: {p.Target} {p.Detail}"));
        }

        return failures;
    }

    [Fact]
    public async Task Reader_editor_and_admin_pages_pass_in_both_themes()
    {
        var id = await SeedAsync();
        await host.CreateUserAsync("a11y@example.com", AppRoles.Admin);
        var failures = new List<string>();

        await using (var anonymous = await host.NewContextAsync())
        {
            var page = await anonymous.NewPageAsync();
            foreach (var path in new[] { "/", "/recipes", "/recipes/accessible-apple-crumble", "/recipes/accessible-apple-crumble/cook",
                         "/search?q=apple", "/account/login" })
            {
                failures.AddRange(await CheckAsync(page, path));
            }
        }

        await using (var admin = await host.NewContextAsync())
        {
            var page = await admin.NewPageAsync();
            await BrowserHost.SignInAsync(page, "a11y@example.com");
            foreach (var path in new[] { "/manage", "/manage/recipes/new", $"/manage/recipes/{id}/edit", "/manage/import", "/manage/kitchen",
                         "/manage/nutrition", "/manage/tags", "/admin", "/admin/users", "/admin/backups", "/admin/health", "/admin/settings",
                         "/admin/audit", "/admin/ai", "/account/2fa", "/account/password", "/shopping" })
            {
                failures.AddRange(await CheckAsync(page, path));
            }
        }

        failures.ShouldBeEmpty(string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task The_checker_finds_planted_problems()
    {
        await using var context = await host.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/account/login");
        await page.EvaluateAsync("""
            () => {
              const m = document.querySelector('main');
              m.insertAdjacentHTML('beforeend',
                '<input name="nolabel">' +
                '<img src="/favicon.ico">' +
                '<button type="button"></button>' +
                '<a href="/x"></a>' +
                '<ul><div>bad child</div></ul>' +
                '<span aria-labelledby="missing-id">x</span>' +
                '<div role="bogus">x</div>' +
                '<a href="/y"><button type="button">inner</button></a>');
              const faint = document.createElement('p'); // the CSP drops inline style attributes, so set styles via the CSSOM
              faint.textContent = 'faint text';
              faint.style.color = '#bbb';
              faint.style.background = '#fff';
              m.append(faint);
            }
            """);
        var problems = (await page.EvaluateAsync<JsonElement>(Script)).Deserialize<List<Problem>>(Json)!;
        problems.Select(p => p.Rule).Distinct().Order().ToList().ShouldBe(
            ["aria-roles", "aria-valid-reference", "button-name", "color-contrast", "image-alt", "label", "link-name", "list", "nested-interactive"],
            customMessage: string.Join(Environment.NewLine, problems.Select(p => $"{p.Rule}: {p.Target} {p.Detail}")));
    }

    [Fact]
    public async Task Keyboard_focus_is_visible_on_links_buttons_and_fields()
    {
        await SeedAsync();
        await using var context = await host.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        var unstyled = new List<string>();
        for (var i = 0; i < 12; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            var focus = await page.EvaluateAsync<JsonElement>("""
                () => {
                  const el = document.activeElement;
                  if (!el || el === document.body) { return null; }
                  const cs = getComputedStyle(el);
                  const outline = cs.outlineStyle !== 'none' && parseFloat(cs.outlineWidth) >= 2;
                  const ring = cs.boxShadow && cs.boxShadow !== 'none';
                  return { tag: el.tagName + ' ' + (el.textContent || el.getAttribute('name') || '').trim().slice(0, 30), shown: outline || ring };
                }
                """);
            if (focus.ValueKind == JsonValueKind.Object && !focus.GetProperty("shown").GetBoolean())
            {
                unstyled.Add(focus.GetProperty("tag").GetString()!);
            }
        }

        unstyled.ShouldBeEmpty(string.Join(", ", unstyled));
    }
}
