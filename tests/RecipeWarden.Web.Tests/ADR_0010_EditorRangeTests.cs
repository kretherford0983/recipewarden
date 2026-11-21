using System.Net;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Web.Tests;

/// <summary>ADR 0010 over HTTP: the editor takes "6-8" and "1 hr 15 min", shows them back, and explains bad input.</summary>
[Trait("Req", "BR-03")]
[Trait("Req", "FR-REC-02")]
public sealed class ADR_0010_EditorRangeTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ranges_and_hours_are_accepted_shown_and_reopened_as_typed()
    {
        using var editor = await factory.SignInAsync("ranges@example.com");
        var response = await editor.CreateRecipeAsync("Pot Roast", extra:
            [("Input.Servings", "Serves 6-8"), ("Input.PrepTime", "20 to 25 min"), ("Input.CookTime", "3-3.5 hours"), ("Input.TotalTime", "")]);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect, await response.TextAsync());
        var slug = response.Headers.Location!.ToString()["/recipes/".Length..];

        using var reader = factory.CreateHttpsClient();
        var page = await reader.GetStringAsync("/recipes/" + slug, Ct);
        page.ShouldContain("<dd>6–8</dd>");
        page.ShouldContain("<dd>20–25 min</dd>");
        page.ShouldContain("<dd>3 hr – 3 hr 30 min</dd>");
        page.ShouldContain("<dd>3 hr 20 min – 3 hr 55 min</dd>"); // total from prep + cook

        var id = await factory.RecipeIdAsync(slug);
        var form = await editor.GetStringAsync($"/manage/recipes/{id}/edit", Ct);
        form.ShouldContain("value=\"6–8\"");
        form.ShouldContain("value=\"20–25 min\"");
        form.ShouldContain("value=\"3 hr – 3 hr 30 min\"");

        // Saving the form unchanged keeps the same values.
        var resave = await editor.PostFormAsync($"/manage/recipes/{id}/edit", ("Input.Title", "Pot Roast"), ("Input.Ingredients", "1 cup flour"),
            ("Input.Steps", "Mix."), ("Input.SourceType", "Manual"), ("Input.Version", "1"), ("Action", "publish"),
            ("Input.Servings", "6–8"), ("Input.PrepTime", "20–25 min"), ("Input.CookTime", "3 hr – 3 hr 30 min"));
        resave.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await reader.GetStringAsync("/recipes/" + slug, Ct)).ShouldContain("<dd>3 hr – 3 hr 30 min</dd>");
    }

    [Fact]
    public async Task Unreadable_servings_and_times_say_what_is_accepted_and_save_nothing()
    {
        using var editor = await factory.SignInAsync("ranges-bad@example.com");
        var response = await editor.CreateRecipeAsync("Mystery Stew", extra:
            [("Input.Servings", "a crowd"), ("Input.CookTime", "overnight"), ("Input.PrepTime", "30-20")]);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.TextAsync();
        html.ShouldContain(ServingsRules.Unreadable);
        html.ShouldContain(TimeText.Unreadable);
        html.ShouldContain(TimeText.HighBelowLow);
        html.ShouldContain("href=\"#Input_CookTime\"");
        html.ShouldContain("value=\"a crowd\""); // the typed text is kept for correcting

        using var reader = factory.CreateHttpsClient();
        (await reader.GetAsync("/recipes/mystery-stew", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Built_in_tags_are_suggested_in_the_editor()
    {
        using var editor = await factory.SignInAsync("ranges-tags@example.com");
        var html = await editor.GetStringAsync("/manage/recipes/new", Ct);
        html.ShouldContain("<option value=\"Homemade Ingredient\">");
        html.ShouldContain("<option value=\"Side Dish\">");
    }
}
