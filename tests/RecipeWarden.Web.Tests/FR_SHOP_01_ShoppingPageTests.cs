using System.Net;
using RecipeWarden.Core.Security;

namespace RecipeWarden.Web.Tests;

/// <summary>
/// FR-SHOP-01 to 04, AC-50, AC-51, D-10, ADR 0028: lists through the pages. Anyone views and ticks (antiforgery
/// required); only editors add, remove and delete.
/// </summary>
[Trait("Req", "FR-SHOP-01")]
public sealed class FR_SHOP_01_ShoppingPageTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string> NewListAsync(HttpClient editor, string name)
    {
        var created = await editor.PostFormAsync("/shopping/new", ("Name", name));
        created.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return created.Headers.Location!.OriginalString;
    }

    private static string ListId(string path) => path.Split('/')[^1];

    [Fact]
    [Trait("Req", "AC-50")]
    public async Task Two_recipes_with_butter_merge_on_the_list_page_with_both_sources()
    {
        using var editor = await factory.SignInAsync("shop-50@example.com");
        var cookies = await editor.AddRecipeAsync("Shop Cookies", "1/2 cup butter\n1 cup sugar");
        var brownies = await editor.AddRecipeAsync("Shop Brownies", "4 Tbsp butter\n2 eggs");
        var list = await NewListAsync(editor, "AC-50 trip");

        // From the recipe page, at 2x for the cookies (½ cup → 1 cup).
        var add = await editor.PostFormAsync("/shopping/new?handler=AddRecipe", ("slug", cookies), ("scale", "2"), ("listId", ListId(list)));
        add.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        add.Headers.Location!.OriginalString.ShouldBe($"/recipes/{cookies}?added={ListId(list)}&scale=2");
        (await editor.GetStringAsync(add.Headers.Location, Ct)).ShouldContain("Added to <a href=\"" + list + "\">AC-50 trip</a>");
        await editor.PostFormAsync("/shopping/new?handler=AddRecipe", ("slug", brownies), ("scale", "1"), ("listId", ListId(list)));

        var html = await editor.GetStringAsync(list, Ct);
        html.ShouldContain("<span class=\"item-amount\">1¼ cups</span>");
        html.ShouldContain("<span class=\"item-package\">(2½ sticks)</span>");
        html.ShouldContain("for Shop Cookies, Shop Brownies");
        html.ShouldContain("Dairy and Eggs");
        html.ShouldContain("Check before you buy"); // sugar
        html.ShouldContain("Shop Cookies (2x)");
    }

    [Fact]
    [Trait("Req", "AC-51")]
    public async Task An_anonymous_reader_ticks_an_item_and_another_device_sees_it()
    {
        using var editor = await factory.SignInAsync("shop-51@example.com");
        var list = await NewListAsync(editor, "AC-51 trip");
        await editor.PostFormAsync(list + "?handler=AddItem", ("item", "2 lemons"));
        await editor.PostFormAsync(list + "?handler=AddItem", ("item", "1 tsp salt"));

        using var phone = factory.CreateHttpsClient();
        var page = await phone.GetStringAsync(list, Ct);
        page.ShouldNotContain("handler=AddItem");      // no edit controls for readers
        page.ShouldNotContain("Delete this list");
        var pantry = page.IndexOf("Check before you buy", StringComparison.Ordinal);
        pantry.ShouldBeGreaterThan(page.IndexOf("lemon", StringComparison.Ordinal));
        page.IndexOf(">salt<", StringComparison.Ordinal).ShouldBeGreaterThan(pantry);

        var key = System.Text.RegularExpressions.Regex.Match(page, "name=\"key\" value=\"([^\"]+)\"").Groups[1].Value;
        key.ShouldStartWith("f:"); // "2 lemons" matched the seeded food
        var tick = await phone.PostFormAsync(list + "?handler=Check", [new("key", key), new("done", "true")], tokenPage: list);
        tick.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        using var laptop = factory.CreateHttpsClient();
        var after = await laptop.GetStringAsync(list, Ct);
        after.ShouldContain("aria-label=\"Untick lemon\"");
        after.ShouldContain("All done — this list will be removed in 15 minutes.");

        // Undo from another device.
        await laptop.PostFormAsync(list + "?handler=Check", [new("key", key), new("done", "false")], tokenPage: list);
        (await phone.GetStringAsync(list, Ct)).ShouldNotContain("All done");
    }

    [Fact]
    [Trait("Req", "SEC-30")]
    public async Task A_tick_without_a_token_or_for_an_unknown_item_changes_nothing()
    {
        await factory.SeedSamplesAsync();
        using var anon = factory.CreateHttpsClient();
        var noToken = await anon.PostAsync("/shopping/1?handler=Check",
            new FormUrlEncodedContent([new("key", "n:onion"), new("done", "true")]), Ct);
        noToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await anon.PostFormAsync("/shopping/1?handler=Check", [new("key", "f:99999"), new("done", "true")], tokenPage: "/shopping/1"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anon.PostFormAsync("/shopping/987654?handler=Check", [new("key", "n:onion"), new("done", "true")], tokenPage: "/shopping/1"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anon.GetStringAsync("/shopping/1", Ct)).ShouldNotContain("All done");
    }

    [Theory]
    [Trait("Req", "SEC-10")]
    [InlineData("AddItem", "item", "3 limes")]
    [InlineData("RemoveItem", "lineId", "1")]
    [InlineData("RemoveRecipe", "listRecipeId", "1")]
    [InlineData("Delete", "x", "1")]
    public async Task Anonymous_readers_cannot_edit_a_list(string handler, string field, string value)
    {
        await factory.SeedSamplesAsync();
        using var anon = factory.CreateHttpsClient();
        var response = await anon.PostFormAsync($"/shopping/1?handler={handler}", [new(field, value)], tokenPage: "/shopping/1");
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldStartWith("https://localhost/account/login");
        var after = await anon.GetStringAsync("/shopping/1", Ct);
        after.ShouldNotContain("lime");
        after.ShouldContain(">onion<"); // the sample recipe's line is still there

        using var reader = await factory.SignInAsync($"shop-reader-{handler}@example.com", role: null);
        (await reader.PostFormAsync($"/shopping/1?handler={handler}", [new(field, value)], tokenPage: "/shopping/1"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Req", "FR-SHOP-04")]
    public async Task A_list_prints_without_navigation_and_exports_as_text_and_markdown()
    {
        using var editor = await factory.SignInAsync("shop-04@example.com");
        var list = await NewListAsync(editor, "Export trip");
        await editor.PostFormAsync(list + "?handler=AddItem", ("item", "2 cups milk"));
        await editor.PostFormAsync(list + "?handler=AddItem", ("item", "*bold* [x](http://evil.example)"));

        using var anon = factory.CreateHttpsClient();
        var print = await anon.GetStringAsync(list + "?handler=Print", Ct);
        print.ShouldNotContain("primary-nav");
        print.ShouldNotContain("site-header");
        print.ShouldContain("whole milk — 2 cups"); // "milk" matched the seeded food

        var text = await anon.GetAsync(list + "?handler=Text&format=txt", Ct);
        text.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
        (await text.Content.ReadAsStringAsync(Ct)).ShouldStartWith("Export trip\n");

        var md = await anon.GetAsync(list + "?handler=Text&format=md", Ct);
        md.Content.Headers.ContentType!.MediaType.ShouldBe("text/markdown");
        var body = await md.Content.ReadAsStringAsync(Ct);
        body.ShouldContain("- [ ] whole milk — 2 cups");
        body.ShouldContain("\\*bold\\*");
        body.ShouldNotContain("[x](");
    }

    [Fact]
    [Trait("Req", "AC-76")]
    public async Task Item_text_is_encoded_on_the_page()
    {
        using var editor = await factory.SignInAsync("shop-76@example.com");
        var list = await NewListAsync(editor, "Encode trip");
        await editor.PostFormAsync(list + "?handler=AddItem", ("item", "1 \"><img src=x onerror=alert(1)> pepper"));
        var html = await editor.GetStringAsync(list, Ct);
        html.ShouldNotContain("<img src=x");
        html.ShouldContain("&quot;&gt;");
    }

    [Fact]
    [Trait("Req", "ADR-0028")]
    public async Task Editors_start_again_from_a_remembered_name_remove_things_and_delete_a_list()
    {
        using var editor = await factory.SignInAsync("shop-again@example.com");
        var list = await NewListAsync(editor, "Weekly shop");
        (await editor.GetStringAsync("/shopping", Ct)).ShouldContain("<a href=\"" + list + "\">Weekly shop</a>");

        await editor.PostFormAsync(list + "?handler=AddItem", ("item", "bread"));
        var withItem = await editor.GetStringAsync(list, Ct);
        withItem.ShouldContain("aria-label=\"Remove bread\"");
        (await editor.PostFormAsync(list + "?handler=AddItem", ("item", ""))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var deleted = await editor.PostFormAsync(list + "?handler=Delete");
        deleted.Headers.Location!.OriginalString.ShouldBe("/shopping?done=deleted");
        (await editor.GetAsync(list, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var index = await editor.GetStringAsync("/shopping", Ct);
        index.ShouldContain("Start again:");
        index.ShouldContain(">Weekly shop</button>");
        (await editor.PostFormAsync("/shopping/new", ("Name", ""))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Adding_a_recipe_to_a_new_list_by_name_and_bad_input()
    {
        using var editor = await factory.SignInAsync("shop-new@example.com");
        var slug = await editor.AddRecipeAsync("Shop Salad", "1 head lettuce");
        var add = await editor.PostFormAsync("/shopping/new?handler=AddRecipe", ("slug", slug), ("newName", "Salad night"), ("listId", "1"));
        var location = add.Headers.Location!.OriginalString;
        location.ShouldStartWith($"/recipes/{slug}?added=");
        var html = await editor.GetStringAsync(location, Ct);
        html.ShouldContain(">Salad night</a>");

        (await editor.PostFormAsync("/shopping/new?handler=AddRecipe", ("slug", "../x"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await editor.PostFormAsync("/shopping/new?handler=AddRecipe", ("slug", slug), ("listId", "987654")))
            .StatusCode.ShouldBe(HttpStatusCode.OK); // the error is shown
        (await editor.PostFormAsync("/shopping/new?handler=AddRecipe", ("slug", slug), ("newName", new string('n', 81))))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Req", "UX-11")]
    public async Task The_recipe_page_offers_the_list_only_to_editors()
    {
        await factory.SeedSamplesAsync();
        using var anon = factory.CreateHttpsClient();
        var reader = await anon.GetStringAsync("/recipes/" + WebTestHelpers.SampleRecipeSlug, Ct);
        reader.ShouldNotContain("Add to shopping list");
        reader.ShouldContain("/recipes/sample-soup/cook\">Cook mode</a>");

        using var editor = await factory.SignInAsync("shop-recipe@example.com");
        var html = await editor.GetStringAsync("/recipes/" + WebTestHelpers.SampleRecipeSlug + "?scale=2", Ct);
        html.ShouldContain("Add to shopping list");
        html.ShouldContain("<input type=\"hidden\" name=\"scale\" value=\"2\">");
        html.ShouldContain("Add at 2x");
        html.ShouldContain("/recipes/sample-soup/print?scale=2\"");
    }

    [Fact]
    [Trait("Req", "ADR-0030")]
    public async Task Editors_set_a_foods_section_pantry_flag_and_package_on_conversions()
    {
        using var editor = await factory.SignInAsync("shop-conv@example.com");
        var html = await editor.GetStringAsync("/manage/conversions?q=kale", Ct);
        html.ShouldContain("handler=UpdateShopping");
        var id = System.Text.RegularExpressions.Regex.Match(html, "id=\"section-(\\d+)\"").Groups[1].Value;

        var bad = await editor.PostFormAsync("/manage/conversions?handler=UpdateShopping", ("id", id), ("section", "Basement"), ("q", "kale"));
        (await bad.Content.ReadAsStringAsync(Ct)).ShouldContain(Core.Shopping.ShoppingRules.BadSection);

        var ok = await editor.PostFormAsync("/manage/conversions?handler=UpdateShopping", ("id", id), ("section", "Produce"),
            ("pantry", "true"), ("packageSize", "1"), ("packageUnit", "lb"), ("packageName", "bunch"), ("q", "kale"));
        ok.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var after = await editor.GetStringAsync("/manage/conversions?q=kale", Ct);
        after.ShouldContain("value=\"bunch\"");
        after.ShouldContain("<option value=\"lb\" selected=\"selected\">lb</option>");

        (await editor.PostFormAsync("/manage/conversions?handler=UpdateShopping", ("id", id), ("packageSize", "abc")))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
