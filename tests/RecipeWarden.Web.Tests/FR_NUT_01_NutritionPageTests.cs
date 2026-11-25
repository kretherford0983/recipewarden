using System.Net;
using System.Text.RegularExpressions;

namespace RecipeWarden.Web.Tests;

/// <summary>
/// FR-NUT-01 to 04, FR-ALG-01 to 03, BR-50 to BR-54, AC-55 to AC-57, ADR 0034 to 0036: nutrition and allergens through
/// the pages.
/// </summary>
[Trait("Req", "FR-NUT-01")]
public sealed partial class FR_NUT_01_NutritionPageTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex("<ul class=\"allergens\"[^>]*>(.*?)</ul>", RegexOptions.Singleline)]
    private static partial Regex Badges();

    private static string BadgeText(string html) => Badges().Match(html) is { Success: true } m ? m.Groups[1].Value : string.Empty;

    [Fact]
    [Trait("Req", "AC-55")]
    [Trait("Req", "FR-ALG-03")]
    public async Task The_recipe_page_shows_calculated_nutrition_with_completeness_and_the_disclaimer()
    {
        using var editor = await factory.SignInAsync("nut-55@example.com");
        var slug = await editor.AddRecipeAsync("Nut Pancakes", "2 cups all-purpose flour\n2 large eggs\n1 cup whole milk\n1 Tbsp zorbleberry jam");

        using var anon = factory.CreateHttpsClient();
        var html = await anon.GetStringAsync("/recipes/" + slug, Ct);
        html.ShouldContain("Calculated from the ingredients");
        html.ShouldContain("75% of ingredients matched");
        html.ShouldNotContain("class=\"badge partial\"");
        html.ShouldContain("Nutrition and allergen information are estimates.");
        html.ShouldContain("<th scope=\"row\">Calories</th>");
        html.ShouldNotContain("/nutrition\">Nutrition and allergens</a>"); // readers get no editor link

        var partial = await editor.AddRecipeAsync("Nut Mystery", "1 cup zorbleberries\n1 cup quixberries\n1 cup whole milk");
        (await anon.GetStringAsync("/recipes/" + partial, Ct)).ShouldContain("<strong class=\"badge partial\">partial</strong>");
    }

    [Fact]
    [Trait("Req", "BR-50")]
    public async Task A_manual_panel_wins_and_can_be_cleared()
    {
        using var editor = await factory.SignInAsync("nut-manual@example.com");
        var slug = await editor.AddRecipeAsync("Nut Manual Soup", "1 cup whole milk");
        var id = await factory.RecipeIdAsync(slug);
        var page = await editor.GetStringAsync($"/manage/recipes/{id}/nutrition", Ct);
        page.ShouldContain("How each ingredient counted");
        page.ShouldContain("counted");

        var bad = await editor.PostFormAsync($"/manage/recipes/{id}/nutrition?handler=Panel", ("Input.Calories", "lots"));
        (await bad.Content.ReadAsStringAsync(Ct)).ShouldContain("Nutrition values must be numbers");

        var saved = await editor.PostFormAsync($"/manage/recipes/{id}/nutrition?handler=Panel", ("Input.Calories", "321"), ("Input.ProteinG", "12.5"));
        saved.Headers.Location!.OriginalString.ShouldBe($"/manage/recipes/{id}/nutrition?saved=panel");
        var recipe = await editor.GetStringAsync("/recipes/" + slug, Ct);
        recipe.ShouldContain("Entered by hand");
        recipe.ShouldContain("<td>321</td>");
        recipe.ShouldContain("<td>12.5 g</td>");

        await editor.PostFormAsync($"/manage/recipes/{id}/nutrition?handler=Panel", ("clear", "1"));
        (await editor.GetStringAsync("/recipes/" + slug, Ct)).ShouldContain("Calculated from the ingredients");
        (await editor.GetAsync("/manage/recipes/999999/nutrition", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await editor.PostFormAsync("/manage/recipes/999999/nutrition?handler=Panel", ("Input.Calories", "1"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Req", "AC-56")]
    [Trait("Req", "BR-52")]
    public async Task Soy_sauce_shows_soy_and_wheat_and_an_override_survives_an_edit()
    {
        using var editor = await factory.SignInAsync("nut-56@example.com");
        var slug = await editor.AddRecipeAsync("Nut Stir Fry", "2 Tbsp soy sauce\n1 cup broccoli");
        var id = await factory.RecipeIdAsync(slug);
        var badges = BadgeText(await editor.GetStringAsync("/recipes/" + slug, Ct));
        badges.ShouldContain(">Soy<");
        badges.ShouldContain(">Wheat<");

        var form = new List<KeyValuePair<string, string>> { new("a_Wheat", "none"), new("a_Gluten", "none"), new("a_Sesame", "may") };
        var saved = await editor.PostFormAsync($"/manage/recipes/{id}/nutrition?handler=Allergens", form, tokenPage: $"/manage/recipes/{id}/nutrition");
        saved.Headers.Location!.OriginalString.ShouldEndWith("saved=allergens");

        var after = BadgeText(await editor.GetStringAsync("/recipes/" + slug, Ct));
        after.ShouldNotContain(">Wheat<");
        after.ShouldContain("Sesame (may contain)");

        // Editing the recipe re-detects, and the override stays.
        var edit = await editor.GetStringAsync($"/manage/recipes/{id}/edit", Ct);
        var version = Regex.Match(edit, "name=\"Input.Version\" value=\"(\\d+)\"").Groups[1].Value;
        (await editor.PostFormAsync($"/manage/recipes/{id}/edit", ("Input.Title", "Nut Stir Fry"), ("Input.Ingredients", "2 Tbsp soy sauce\n1 cup broccoli\n1 egg"),
            ("Input.Steps", "Fry."), ("Input.SourceType", "Manual"), ("Input.Version", version), ("Action", "publish"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var edited = BadgeText(await editor.GetStringAsync("/recipes/" + slug, Ct));
        edited.ShouldNotContain(">Wheat<");
        edited.ShouldContain(">Egg<");
        var page = await editor.GetStringAsync($"/manage/recipes/{id}/nutrition", Ct);
        page.ShouldContain("<option value=\"none\" selected=\"selected\">Not present</option>");
    }

    [Fact]
    [Trait("Req", "AC-57")]
    [Trait("Req", "FR-NUT-03")]
    public async Task Gochujang_is_reported_mapped_by_search_and_every_recipe_gets_complete()
    {
        using var editor = await factory.SignInAsync("nut-57@example.com");
        var slugs = new List<string>();
        foreach (var t in new[] { "Nut Bibimbap", "Nut Tteokbokki", "Nut Wings" })
        {
            slugs.Add(await editor.AddRecipeAsync(t, "2 Tbsp gochujang\n1 cup water"));
        }

        var report = await editor.GetStringAsync("/manage/nutrition", Ct);
        report.ShouldContain("<td>gochujang</td>");
        report.ShouldContain("USDA FoodData Central (CC0): SR Legacy 2018-04; Foundation Foods 2026-04 and FNDDS 2024-10, 13,694 foods.");
        (await editor.GetStringAsync("/recipes/" + slugs[0], Ct)).ShouldContain("50% of ingredients matched");

        var search = await editor.GetStringAsync("/manage/nutrition?map=gochujang&q=chili+sauce", Ct);
        var nutrientId = Regex.Match(search, "name=\"nutrientId\" value=\"(\\d+)\"").Groups[1].Value;
        nutrientId.ShouldNotBeEmpty();
        search.ShouldMatch("<span class=\"hint\">[^<]*(SR Legacy|Foundation|FNDDS)</span>"); // ADR 0052: the dataset is named
        var mapped = await editor.PostFormAsync("/manage/nutrition?handler=Map", ("name", "gochujang"), ("nutrientId", nutrientId));
        mapped.Headers.Location!.OriginalString.ShouldBe("/manage/nutrition?done=mapped");
        foreach (var slug in slugs)
        {
            (await editor.GetStringAsync("/recipes/" + slug, Ct)).ShouldContain("100% of ingredients matched");
        }

        (await editor.GetStringAsync("/manage/nutrition", Ct)).ShouldNotContain("<td>gochujang</td>");
        var again = await editor.PostFormAsync("/manage/nutrition?handler=Map", ("name", "gochujang"), ("nutrientId", nutrientId));
        (await again.Content.ReadAsStringAsync(Ct)).ShouldContain("class=\"notice error\"");
        (await editor.GetStringAsync("/manage/nutrition?q=zzzzqqq", Ct)).ShouldContain("No foods match.");
    }

    [Fact]
    [Trait("Req", "FR-NUT-04")]
    [Trait("Req", "AC-57")]
    public async Task A_label_food_is_added_per_100_g_and_its_name_is_encoded()
    {
        using var editor = await factory.SignInAsync("nut-label@example.com");
        var added = await editor.PostFormAsync("/manage/nutrition?handler=Label", ("labelName", "<b>Granola</b> Crunch"), ("serving", "30"),
            ("servingUnit", "g"), ("Label.Calories", "110"), ("Label.FatG", "4"));
        added.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var page = await editor.GetStringAsync(added.Headers.Location!.OriginalString, Ct);
        page.ShouldContain("Food added.");
        page.ShouldContain("&lt;b&gt;Granola&lt;/b&gt; Crunch (label)");
        page.ShouldContain("<td>367 kcal</td>");

        (await (await editor.PostFormAsync("/manage/nutrition?handler=Label", ("labelName", "X"), ("serving", "abc"), ("Label.Calories", "1")))
            .Content.ReadAsStringAsync(Ct)).ShouldContain("Give the label&#x27;s serving size");
        (await (await editor.PostFormAsync("/manage/nutrition?handler=Label", ("labelName", "X"), ("serving", "30")))
            .Content.ReadAsStringAsync(Ct)).ShouldContain(Core.Nutrition.NutritionRules.NoValues);
    }

    [Fact]
    [Trait("Req", "BR-53")]
    public async Task Free_of_filters_the_list_and_unverified_recipes_need_the_tick()
    {
        using var editor = await factory.SignInAsync("nut-53@example.com");
        await editor.AddRecipeAsync("Freeof Milk Soup", "1 cup whole milk");
        await editor.AddRecipeAsync("Freeof Water Soup", "1 cup water");
        await editor.AddRecipeAsync("Freeof Mystery Soup", "1 cup zorbleberries");

        using var anon = factory.CreateHttpsClient();
        var html = await anon.GetStringAsync("/search?q=freeof&free=milk", Ct);
        html.ShouldContain("Freeof Water Soup");
        html.ShouldNotContain("Freeof Milk Soup");
        html.ShouldNotContain("Freeof Mystery Soup");
        html.ShouldContain("<input type=\"checkbox\" name=\"free\" value=\"milk\" checked=\"checked\">");

        var unverified = await anon.GetStringAsync("/search?q=freeof&free=milk&unverified=1", Ct);
        unverified.ShouldContain("Freeof Mystery Soup");
        unverified.ShouldNotContain("Freeof Milk Soup");
        (await anon.GetStringAsync("/recipes?free=bogus&free=milk", Ct)).ShouldNotContain("Freeof Milk Soup");
    }
}
