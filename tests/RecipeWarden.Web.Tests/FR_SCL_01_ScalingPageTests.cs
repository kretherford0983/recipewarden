using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Web.Tests;

/// <summary>
/// FR-SCL-01/02/03, FR-MEAS-01/03/04/06/07, AC-30/31/34/35/36, ADR 0014: the recipe page scaled and converted from
/// the query string, anonymously, GET only, without changing the stored recipe.
/// </summary>
[Trait("Req", "FR-SCL-01")]
public sealed class FR_SCL_01_ScalingPageTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Ingredients = "2 cups all-purpose flour\n1.5 lb boneless skinless chicken breasts\n3 eggs\na pinch of salt\n" +
                                       "salt and pepper to taste\n4 cups cooked shredded chicken\n1 (15 oz) can black beans, drained";

    private async Task<string> RecipeAsync(string title, string steps = "1. **Preheat oven**\n   Preheat oven to 375°F.\n2. Bake.",
        bool precision = false)
    {
        using var editor = await factory.SignInAsync("scale-editor@example.com");
        var extra = new List<(string, string)> { ("Input.Servings", "4") };
        if (precision)
        {
            extra.Add(("Input.IsPrecision", "true"));
        }

        var response = await editor.CreateRecipeAsync(title, Ingredients, steps, extra: extra.ToArray());
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect, await response.TextAsync());
        return response.Headers.Location!.ToString()["/recipes/".Length..];
    }

    private async Task<string> GetAsync(string url, string? cookie = null, HttpClient? client = null)
    {
        using var anonymous = factory.CreateHttpsClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        var response = await (client ?? anonymous).SendAsync(request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, url);
        return System.Net.WebUtility.HtmlDecode(await response.TextAsync());
    }

    [Theory]
    [Trait("Req", "AC-30")]
    [InlineData("scale=1.5", "serves 6", "3 cups all-purpose flour")]
    [InlineData("scale=2", "serves 8", "4 cups all-purpose flour")]
    [InlineData("scale=10", "serves 40", "20 cups all-purpose flour")]
    [InlineData("servings=8", "serves 8", "4 cups all-purpose flour")]
    public async Task Anonymous_readers_can_scale_and_servings_follow(string query, string servings, string flour)
    {
        var slug = await RecipeAsync("Scale Page " + query);
        var html = await GetAsync($"/recipes/{slug}?{query}");

        html.ShouldContain(servings);
        html.ShouldContain(flour);
        html.ShouldContain("The saved recipe is unchanged.");
        html.ShouldContain("salt and pepper, to taste");
    }

    [Fact]
    [Trait("Req", "FR-SCL-03")]
    public async Task Scaling_never_changes_the_stored_recipe()
    {
        var slug = await RecipeAsync("Scale Unchanged");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await db.Recipes.AsNoTracking().Where(r => r.Slug == slug).Select(r => new { r.Version, r.UpdatedAt, r.Servings }).SingleAsync(Ct);

        await GetAsync($"/recipes/{slug}?scale=3&units=metric");
        await GetAsync($"/recipes/{slug}?servings=40");

        (await db.Recipes.AsNoTracking().Where(r => r.Slug == slug).Select(r => new { r.Version, r.UpdatedAt, r.Servings }).SingleAsync(Ct))
            .ShouldBe(before);
    }

    [Theory]
    [InlineData("scale=500", ScaleRules.OutOfRange)]
    [InlineData("scale=abc", ScaleRules.NotANumber)]
    [InlineData("servings=lots", ScaleRules.ServingsNotANumber)]
    public async Task Bad_scale_values_show_the_recipe_as_written_with_a_message(string query, string message)
    {
        var slug = await RecipeAsync("Scale Bad " + query);
        var html = await GetAsync($"/recipes/{slug}?{query}");
        html.ShouldContain(message);
        html.ShouldContain("2 cups all-purpose flour");
    }

    [Fact]
    [Trait("Req", "AC-31")]
    public async Task Scale_from_what_I_have()
    {
        var slug = await RecipeAsync("Scale From");
        var html = await GetAsync($"/recipes/{slug}?from=1&have=3+lb");

        html.ShouldContain("Scaled 2x (from 3 lb boneless skinless chicken breasts)");
        html.ShouldContain("4 cups all-purpose flour");

        (await GetAsync($"/recipes/{slug}?from=0&have=3+eggs")).ShouldContain(Core.Measurement.IngredientFactor.DifferentKinds);
    }

    [Fact]
    [Trait("Req", "AC-34")]
    public async Task Pinch_at_10x_and_to_taste_unchanged()
    {
        var slug = await RecipeAsync("Scale Pinch");
        var html = await GetAsync($"/recipes/{slug}?scale=10");
        html.ShouldContain("about ⅝ tsp salt (~10 pinches)");
        html.ShouldContain("salt and pepper, to taste");
    }

    [Fact]
    [Trait("Req", "AC-35")]
    public async Task Cooked_chicken_shows_the_raw_estimate()
    {
        var slug = await RecipeAsync("Scale Yield");
        (await GetAsync($"/recipes/{slug}")).ShouldContain("About 1½–1¾ lb raw chicken breast to buy (boneless breast, simmered, about 70–88% cooking yield).");
    }

    [Fact]
    [Trait("Req", "AC-36")]
    public async Task Eggs_and_cans_at_1_5x()
    {
        var plain = await RecipeAsync("Scale Eggs");
        var baking = await RecipeAsync("Scale Eggs Baking", precision: true);

        var html = await GetAsync($"/recipes/{plain}?scale=1.5");
        html.ShouldContain("4 eggs + 1 yolk");
        html.ShouldContain("1 can (15 oz) + about ½ more black beans, drained");
        html.ShouldNotContain("4.5 eggs");
        (await GetAsync($"/recipes/{baking}?scale=1.5")).ShouldContain("4½ eggs (4 eggs + 2 Tbsp beaten egg)");
    }

    [Fact]
    [Trait("Req", "FR-MEAS-01")]
    public async Task Units_choice_converts_amounts_and_temperatures()
    {
        var slug = await RecipeAsync("Scale Units");

        var weight = await GetAsync($"/recipes/{slug}?units=weight");
        weight.ShouldContain("250 g all-purpose flour");
        weight.ShouldContain("(2 cups)");

        var metric = await GetAsync($"/recipes/{slug}?units=metric");
        metric.ShouldContain("473 ml all-purpose flour");
        metric.ShouldContain("Preheat oven to 190°C.");
        metric.ShouldContain("Preheat oven</strong>");
    }

    [Fact]
    [Trait("Req", "BR-29")]
    public async Task Above_2x_a_note_warns_about_cook_times()
    {
        var slug = await RecipeAsync("Scale Time Note");
        (await GetAsync($"/recipes/{slug}?scale=3")).ShouldContain(ScaleRules.TimeNote);
        (await GetAsync($"/recipes/{slug}?scale=2")).ShouldNotContain(ScaleRules.TimeNote);
    }

    [Fact]
    public async Task Toolbar_forms_are_get_forms_that_htmx_swaps_in_place()
    {
        var slug = await RecipeAsync("Scale Toolbar");
        using var client = factory.CreateHttpsClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/recipes/{slug}?scale=2");
        request.Headers.Add("HX-Request", "true");
        var response = await client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.TextAsync();

        html.ShouldContain("id=\"recipe-body\"");
        html.ShouldContain("hx-select=\"#recipe-body\"");
        html.ShouldContain($"action=\"/recipes/{slug}\"");
        html.ShouldNotContain("<form method=\"post\" action=\"/recipes/");
    }

    [Fact]
    [Trait("Req", "FR-UI-02")]
    public async Task Default_units_and_scale_come_from_cookies_and_the_query_wins()
    {
        var slug = await RecipeAsync("Scale Cookie");
        var html = await GetAsync($"/recipes/{slug}", cookie: "rw-units=metric; rw-scale=2");
        html.ShouldContain("Scaled 2x (your default)");
        html.ShouldContain("946 ml all-purpose flour");

        (await GetAsync($"/recipes/{slug}?units=as-written&scale=1", cookie: "rw-units=metric; rw-scale=2"))
            .ShouldContain("2 cups all-purpose flour");
        (await GetAsync($"/recipes/{slug}", cookie: "rw-scale=5000")).ShouldContain("2 cups all-purpose flour");
    }

    [Fact]
    [Trait("Req", "FR-UI-02")]
    public async Task Signed_in_preferences_are_saved_to_the_profile_and_used()
    {
        var slug = await RecipeAsync("Scale Profile");
        using var client = await factory.SignInAsync("prefs-user@example.com", role: null);
        var token = await RecipeWardenFactory.GetAntiforgeryTokenAsync(client, "/");

        using (var save = new HttpRequestMessage(HttpMethod.Post, "/account/preferences")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["units"] = "weight", ["scale"] = "0.5" }),
        })
        {
            save.Headers.Add("RequestVerificationToken", token);
            (await client.SendAsync(save, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var html = await GetAsync($"/recipes/{slug}", cookie: "rw-units=metric", client: client);
        html.ShouldContain("Scaled 0.5x (your default)");
        html.ShouldContain("125 g all-purpose flour");
    }

    [Theory]
    [InlineData("purple", "1")]
    [InlineData("metric", "500")]
    [InlineData("metric", "abc")]
    public async Task Bad_preference_values_are_rejected(string units, string scale)
    {
        using var client = await factory.SignInAsync("prefs-bad@example.com", role: null);
        var token = await RecipeWardenFactory.GetAntiforgeryTokenAsync(client, "/");
        using var save = new HttpRequestMessage(HttpMethod.Post, "/account/preferences")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["units"] = units, ["scale"] = scale }),
        };
        save.Headers.Add("RequestVerificationToken", token);
        (await client.SendAsync(save, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Req", "SEC-30")]
    public async Task Preferences_post_without_a_token_is_rejected()
    {
        using var client = await factory.SignInAsync("prefs-csrf@example.com", role: null);
        var response = await client.PostAsync("/account/preferences",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["units"] = "metric" }), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_old_slug_redirect_keeps_the_scale()
    {
        using var editor = await factory.SignInAsync("scale-editor@example.com");
        var slug = await RecipeAsync("Scale Rename");
        var id = await factory.RecipeIdAsync(slug);
        var edit = await editor.PostFormAsync($"/manage/recipes/{id}/edit",
            ("Input.Title", "Scale Renamed"), ("Input.Ingredients", "1 cup flour"), ("Input.Steps", "Mix."),
            ("Input.SourceType", "Manual"), ("Input.Version", "1"), ("Action", "publish"));
        edit.StatusCode.ShouldBe(HttpStatusCode.Redirect, await edit.TextAsync());

        using var client = factory.CreateHttpsClient();
        var response = await client.GetAsync($"/recipes/{slug}?scale=2", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.MovedPermanently);
        response.Headers.Location!.ToString().ShouldBe("/recipes/scale-renamed?scale=2");
    }
}
