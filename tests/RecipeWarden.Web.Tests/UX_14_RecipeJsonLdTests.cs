using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using RecipeWarden.Core.Organization;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Content;

namespace RecipeWarden.Web.Tests;

/// <summary>
/// UX-14, ADR 0050: recipe pages carry schema.org Recipe JSON-LD, as recipe sites do. SEC-22/SEC-23: it is the only
/// inline script element, it is data (application/ld+json), and recipe text cannot break out of it.
/// </summary>
[Trait("Req", "UX-14")]
[Trait("Req", "SEC-22")]
public sealed partial class UX_14_RecipeJsonLdTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> PageAsync(string title, string ingredients, string steps, params (string, string)[] extra)
    {
        using var editor = await factory.SignInAsync("jsonld-editor@example.com");
        var created = await editor.CreateRecipeAsync(title, ingredients, steps, extra: extra);
        created.StatusCode.ShouldBe(HttpStatusCode.Redirect, await created.TextAsync());
        return await editor.GetStringAsync(created.Headers.Location!.ToString(), Ct); // raw, not decoded
    }

    private static JsonElement Data(string html)
    {
        var blocks = Block().Matches(html);
        blocks.Count.ShouldBe(1);
        return JsonDocument.Parse(blocks[0].Groups[1].Value).RootElement.Clone();
    }

    [Fact]
    public async Task A_recipe_page_carries_its_recipe_as_json_ld()
    {
        var html = await PageAsync("Ld Chili", "1 lb beef\n- 2 cups beans", "Brown the beef.\nSimmer with the beans.",
            ("Input.Servings", "4"), ("Input.PrepTime", "15"), ("Input.CookTime", "1 hr 15 min"), ("Input.Description", "A **hearty** chili."));
        var data = Data(html);

        data.GetProperty("@context").GetString().ShouldBe("https://schema.org");
        data.GetProperty("@type").GetString().ShouldBe("Recipe");
        data.GetProperty("name").GetString().ShouldBe("Ld Chili");
        data.GetProperty("description").GetString().ShouldBe("A hearty chili.");
        data.GetProperty("recipeYield").GetString().ShouldBe("4 servings"); // as in Apple's HomeView example
        data.GetProperty("url").GetString()!.ShouldEndWith("/recipes/ld-chili");
        data.GetProperty("url").GetString()!.ShouldStartWith("http");
        data.GetProperty("prepTime").GetString().ShouldBe("PT15M");
        data.GetProperty("cookTime").GetString().ShouldBe("PT1H15M");
        data.GetProperty("recipeIngredient").EnumerateArray().Select(e => e.GetString()).ShouldBe(["1 lb beef", "2 cups beans"]);
        var steps = data.GetProperty("recipeInstructions").EnumerateArray().ToList();
        steps.Count.ShouldBe(2);
        steps[0].GetProperty("@type").GetString().ShouldBe("HowToStep");
        steps[1].GetProperty("text").GetString().ShouldBe("Simmer with the beans.");
        steps[0].TryGetProperty("name", out _).ShouldBeFalse(); // no empty values
        data.TryGetProperty("keywords", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Recipe_text_cannot_break_out_of_the_data_block()
    {
        const string Evil = "Pwn </script><script>alert(1)</script> & 'q' \"d\" <!--";
        var html = await PageAsync(Evil, "1 cup </script><img src=x onerror=alert(2)>", "Mix </script>.");

        var json = Block().Match(html).Groups[1].Value;
        json.ShouldNotContain("<");
        json.ShouldNotContain(">");
        json.ShouldNotContain("&");
        Data(html).GetProperty("name").GetString().ShouldBe(Evil); // the data itself is intact

        // The only inline script element on the page is the data block (SEC-23).
        InlineScript().Matches(html).Cast<Match>().ShouldAllBe(m => m.Value.Contains("application/ld+json"));
        InlineScript().Count(html).ShouldBe(1);
    }

    [Fact]
    public async Task Other_pages_have_no_json_ld()
    {
        using var client = factory.CreateHttpsClient();
        (await client.GetStringAsync("/", Ct)).ShouldNotContain("application/ld+json");
    }

    private static RecipeView Sample() => new()
    {
        Id = 7,
        Slug = "green-curry",
        Title = "Green Curry",
        Description = "A **fragrant** curry.\n\nGood with [jasmine rice](https://example.com), and better the next day when the flavours settle.",
        Servings = 4,
        ServingsMax = 6,
        PrepMinutes = 20,
        CookMinutes = 25,
        TotalMinutes = 45,
        SourceAuthor = "Ana",
        FinishImageId = "abc123",
        UpdatedAt = new DateTimeOffset(2026, 11, 20, 9, 30, 0, TimeSpan.Zero),
        Tags = [new("Dinner", "dinner", TagType.Meal), new("Main", "main", TagType.Course), new("Thai", "thai", TagType.Cuisine),
                new("Weeknight", "weeknight", null), new("Spicy", "spicy", TagType.Diet)],
        Ingredients =
        [
            new(null, [new("400 ml coconut milk", "- 400 ml coconut milk", false, false)]),
            new("For the paste", [new("2 tbsp green curry paste", "2 tbsp green curry paste", false, false)]),
        ],
        Steps =
        [
            new(null, [new(null, "Fry the paste in a splash of coconut cream.", "step1img")]),
            new("To finish", [new("Simmer", "Add the rest of the coconut milk and simmer."), new(null, "Season with fish sauce.")]),
        ],
    };

    [Fact]
    [Trait("Req", "UX-14")]
    public void The_data_follows_apples_and_googles_recipe_guidance()
    {
        var data = JsonDocument.Parse(RecipeJsonLd.Json(Sample(), "https://kitchen.example", 412.6m)).RootElement;

        data.GetProperty("url").GetString().ShouldBe("https://kitchen.example/recipes/green-curry");
        data.GetProperty("image").GetString().ShouldBe("https://kitchen.example/media/abc123");
        data.GetProperty("description").GetString().ShouldBe(
            "A fragrant curry. Good with jasmine rice, and better the next day when the flavours settle."); // whole, plain
        data.GetProperty("recipeYield").GetString().ShouldBe("4–6 servings");
        data.GetProperty("prepTime").GetString().ShouldBe("PT20M");
        data.GetProperty("cookTime").GetString().ShouldBe("PT25M");
        data.GetProperty("totalTime").GetString().ShouldBe("PT45M");
        data.GetProperty("author").GetProperty("name").GetString().ShouldBe("Ana");
        data.GetProperty("dateModified").GetString().ShouldBe("2026-11-20");

        // Typed tags go to category and cuisine, and are not repeated as keywords (Google's guidance).
        data.GetProperty("recipeCategory").GetString().ShouldBe("Dinner, Main");
        data.GetProperty("recipeCuisine").GetString().ShouldBe("Thai");
        data.GetProperty("keywords").GetString().ShouldBe("Weeknight, Spicy");

        data.GetProperty("recipeIngredient").EnumerateArray().Select(e => e.GetString())
            .ShouldBe(["400 ml coconut milk", "2 tbsp green curry paste"]);

        // An unnamed step section is plain steps; a named one is a HowToSection; step photos have full addresses.
        var steps = data.GetProperty("recipeInstructions").EnumerateArray().ToList();
        steps.Count.ShouldBe(2);
        steps[0].GetProperty("@type").GetString().ShouldBe("HowToStep");
        steps[0].GetProperty("image").GetString().ShouldBe("https://kitchen.example/media/step1img");
        steps[1].GetProperty("@type").GetString().ShouldBe("HowToSection");
        steps[1].GetProperty("name").GetString().ShouldBe("To finish");
        var inner = steps[1].GetProperty("itemListElement").EnumerateArray().ToList();
        inner.Select(i => i.GetProperty("text").GetString()).ShouldBe(["Add the rest of the coconut milk and simmer.", "Season with fish sauce."]);
        inner[0].GetProperty("name").GetString().ShouldBe("Simmer");
        inner[1].TryGetProperty("name", out _).ShouldBeFalse();

        var nutrition = data.GetProperty("nutrition");
        nutrition.GetProperty("@type").GetString().ShouldBe("NutritionInformation");
        nutrition.GetProperty("calories").GetString().ShouldBe("413 calories");

        data.TryGetProperty("aggregateRating", out _).ShouldBeFalse(); // a household's own rating is not a review
    }

    [Theory]
    [Trait("Req", "UX-14")]
    [InlineData(45, 20, 25, "PT45M")]   // a total someone typed wins
    [InlineData(null, 20, 25, "PT45M")] // otherwise prep + cook, which HomeView shows on its first screen
    [InlineData(null, 20, null, null)]  // prep alone is not a total
    [InlineData(null, null, 25, null)]
    public void Total_time_falls_back_to_prep_plus_cook(int? total, int? prep, int? cook, string? expected)
    {
        var recipe = Sample() with { TotalMinutes = total, PrepMinutes = prep, CookMinutes = cook };
        var data = JsonDocument.Parse(RecipeJsonLd.Json(recipe, "https://kitchen.example", null)).RootElement;
        (data.TryGetProperty("totalTime", out var value) ? value.GetString() : null).ShouldBe(expected);
    }

    [Fact]
    [Trait("Req", "UX-14")]
    public void Unknown_values_are_left_out()
    {
        var bare = Sample() with { Description = null, FinishImageId = null, SourceAuthor = null, Tags = [], PrepMinutes = null };
        var data = JsonDocument.Parse(RecipeJsonLd.Json(bare, "https://kitchen.example", null)).RootElement;
        foreach (var gone in new[] { "description", "image", "author", "prepTime", "recipeCategory", "recipeCuisine", "keywords", "nutrition" })
        {
            data.TryGetProperty(gone, out _).ShouldBeFalse(gone);
        }
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, null)]
    [InlineData(15, "PT15M")]
    [InlineData(60, "PT1H")]
    [InlineData(75, "PT1H15M")]
    [InlineData(150, "PT2H30M")]
    public void Durations_are_iso_8601(int? minutes, string? expected) => RecipeJsonLd.Duration(minutes).ShouldBe(expected);

    [GeneratedRegex("""<script type="application/ld\+json">(.*?)</script>""", RegexOptions.Singleline)]
    private static partial Regex Block();

    [GeneratedRegex("""<script(?![^>]*\bsrc=)[^>]*>""")]
    private static partial Regex InlineScript();
}
