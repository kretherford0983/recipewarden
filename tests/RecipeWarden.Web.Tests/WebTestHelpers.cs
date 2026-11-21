using System.Net;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Web.Tests;

/// <summary>Form posts with antiforgery, and sample data for route tests.</summary>
internal static class WebTestHelpers
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Posts a form the way the browser does: with the user's antiforgery token.</summary>
    public static async Task<HttpResponseMessage> PostFormAsync(this HttpClient client, string url, IEnumerable<KeyValuePair<string, string>> fields,
        string tokenPage = "/manage")
    {
        var token = await RecipeWardenFactory.GetAntiforgeryTokenAsync(client, tokenPage);
        var all = fields.Append(new KeyValuePair<string, string>("__RequestVerificationToken", token));
        return await client.PostAsync(url, new FormUrlEncodedContent(all), Ct);
    }

    public static Task<HttpResponseMessage> PostFormAsync(this HttpClient client, string url, params (string Key, string Value)[] fields) =>
        client.PostFormAsync(url, fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));

    /// <summary>Uses the real editor form. Returns the response (a redirect when saved).</summary>
    public static Task<HttpResponseMessage> CreateRecipeAsync(this HttpClient editor, string title, string ingredients = "1 cup flour",
        string steps = "Mix.", string action = "publish", string? tags = null, params (string Key, string Value)[] extra)
    {
        var fields = new List<(string, string)>
        {
            ("Input.Title", title),
            ("Input.Ingredients", ingredients),
            ("Input.Steps", steps),
            ("Input.Tags", tags ?? string.Empty),
            ("Input.SourceType", "Manual"),
            ("Action", action),
        };
        fields.RemoveAll(f => extra.Any(e => e.Key == f.Item1)); // a test's value replaces the default
        fields.AddRange(extra);
        return editor.PostFormAsync("/manage/recipes/new", fields.ToArray());
    }

    /// <summary>Creates a recipe through the form and returns its slug.</summary>
    public static async Task<string> AddRecipeAsync(this HttpClient editor, string title, string ingredients = "1 cup flour",
        string steps = "Mix.", string? tags = null)
    {
        var response = await editor.CreateRecipeAsync(title, ingredients, steps, "publish", tags);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync(Ct));
        var location = response.Headers.Location!.ToString();
        location.ShouldStartWith("/recipes/");
        return location["/recipes/".Length..];
    }

    public static async Task<int> RecipeIdAsync(this RecipeWardenFactory factory, string slug)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var lookup = await scope.ServiceProvider.GetRequiredService<RecipeService>().GetForReadingAsync(slug, true, Ct);
        return lookup.Recipe.ShouldNotBeNull().Id;
    }

    public static async Task<string> TextAsync(this HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync(Ct);

    /// <summary>Route-test data: a published recipe (Id 1), a tag and a collection, created once per host.</summary>
    public static async Task SeedSamplesAsync(this RecipeWardenFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var recipes = scope.ServiceProvider.GetRequiredService<RecipeService>();
        if ((await recipes.GetForReadingAsync(SampleRecipeSlug, true, Ct)).Recipe is not null)
        {
            return;
        }

        var outcome = await recipes.CreateAsync(new RecipeEdit
        {
            Title = "Sample Soup",
            IngredientsText = "1 onion",
            StepsText = "Cook.",
            Tags = [SampleTagSlug],
            WantsPublish = true,
        }, "seed", Ct);
        var collections = scope.ServiceProvider.GetRequiredService<CollectionService>();
        var c = await collections.CreateAsync("Sample List", null, "seed", Ct);
        await collections.AddRecipeAsync(c.Id, outcome.Id, Ct);
    }

    public const string SampleRecipeSlug = "sample-soup";
    public const string SampleTagSlug = "sample";
    public const string SampleCollectionSlug = "sample-list";

    /// <summary>A real URL for a route pattern, using the seeded samples.</summary>
    public static string SampleUrl(string route) => route
        .Replace("/recipes/{slug}", "/recipes/" + SampleRecipeSlug, StringComparison.Ordinal)
        .Replace("/tags/{slug}", "/tags/" + SampleTagSlug, StringComparison.Ordinal)
        .Replace("/collections/{slug}", "/collections/" + SampleCollectionSlug, StringComparison.Ordinal)
        .Replace("{id:int}", "1", StringComparison.Ordinal);
}
