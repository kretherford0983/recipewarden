using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        data.GetProperty("recipeYield").GetString().ShouldBe("4");
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
