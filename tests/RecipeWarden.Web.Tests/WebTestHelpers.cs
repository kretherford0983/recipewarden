using System.Net;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Import;
using RecipeWarden.Infrastructure.Media;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Infrastructure.Shopping;

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

        // M4: a published recipe's image with a known id (/media), and an import waiting for review (job 1).
        var data = scope.ServiceProvider.GetRequiredService<DataOptions>();
        var (encoded, _) = ImageEncoder.Encode(SampleJpeg());
        var folder = Path.Combine(data.MediaDirectory, SampleImageId[..2]);
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, SampleImageId + ".webp"), encoded!.Full, Ct);
        await File.WriteAllBytesAsync(Path.Combine(folder, SampleImageId + ".thumb.webp"), encoded.Thumb, Ct);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Images.Add(new Image { Id = SampleImageId, StoragePath = SampleImageId[..2] + "/" + SampleImageId + ".webp", Width = encoded.Width, Height = encoded.Height });
        await db.SaveChangesAsync(Ct);
        await recipes.SetFinishImageAsync(outcome.Id, SampleImageId, "seed", Ct);
        await scope.ServiceProvider.GetRequiredService<ImportService>()
            .StartPasteAsync("Sample Import\n\nIngredients\n1 egg\n\nSteps\nBoil.", null, false, "seed", Ct);

        // M5: a shopping list (id 1) with the sample recipe on it.
        var shopping = scope.ServiceProvider.GetRequiredService<ShoppingService>();
        var list = await shopping.CreateAsync("Sample Trip", "seed", Ct);
        await shopping.AddRecipeAsync(list.Id!.Value, SampleRecipeSlug, 1m, Ct);
    }

    public const string SampleImageId = "0123456789abcdef0123456789abcdef";

    /// <summary>A small JPEG made with SkiaSharp (no binary fixtures).</summary>
    public static byte[] SampleJpeg(int width = 64, int height = 48)
    {
        using var bitmap = new SkiaSharp.SKBitmap(width, height);
        bitmap.Erase(new SkiaSharp.SKColor(120, 160, 60));
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 85);
        return encoded.ToArray();
    }

    public const string SampleRecipeSlug = "sample-soup";
    public const string SampleTagSlug = "sample";
    public const string SampleCollectionSlug = "sample-list";

    /// <summary>A real URL for a route pattern, using the seeded samples.</summary>
    public static string SampleUrl(string route) => route
        .Replace("/recipes/{slug}", "/recipes/" + SampleRecipeSlug, StringComparison.Ordinal)
        .Replace("/tags/{slug}", "/tags/" + SampleTagSlug, StringComparison.Ordinal)
        .Replace("/collections/{slug}", "/collections/" + SampleCollectionSlug, StringComparison.Ordinal)
        .Replace("/media/{id}/{variant?}", "/media/" + SampleImageId, StringComparison.Ordinal)
        .Replace("{id:int}", "1", StringComparison.Ordinal)
        .Replace("{format}", "md", StringComparison.Ordinal);
}

/// <summary>Multipart form posts (file uploads) with the user's antiforgery token.</summary>
internal static class MultipartHelpers
{
    public static async Task<HttpResponseMessage> PostFilesAsync(this HttpClient client, string url, IEnumerable<(string Field, string FileName, byte[] Bytes)> files,
        params (string Key, string Value)[] fields)
    {
        var token = await RecipeWardenFactory.GetAntiforgeryTokenAsync(client, "/manage");
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        foreach (var (key, value) in fields)
        {
            content.Add(new StringContent(value), key);
        }

        foreach (var (field, name, bytes) in files)
        {
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            content.Add(part, field, name);
        }

        return await client.PostAsync(url, content, TestContext.Current.CancellationToken);
    }
}
