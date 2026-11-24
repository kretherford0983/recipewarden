using System.Net;
using System.Text.RegularExpressions;

namespace RecipeWarden.Web.Tests;

/// <summary>
/// FR-SHOP-05, BR-43 to BR-47, AC-51, AC-53, AC-125: the staples page, "Check before you buy" and the walk-through.
/// Each test has its own factory: the staples list is shared by the whole site.
/// </summary>
[Trait("Req", "FR-SHOP-05")]
public sealed partial class FR_SHOP_05_StaplePageTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex("name=\"key\" value=\"(f:\\d+)\"")]
    private static partial Regex KeyField();

    private static async Task<string> NewListAsync(HttpClient editor, string name, bool check = false)
    {
        var created = check
            ? await editor.PostFormAsync("/shopping/new", ("Name", name), ("StaplesCheck", "true"))
            : await editor.PostFormAsync("/shopping/new", ("Name", name));
        created.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return created.Headers.Location!.OriginalString;
    }

    [Fact]
    [Trait("Req", "AC-51")]
    public async Task A_reader_answers_need_it_and_have_it_for_the_roux_staples()
    {
        await using var factory = new RecipeWardenFactory();
        using var editor = await factory.SignInAsync("staple-roux@example.com");
        (await editor.PostFormAsync("/manage/staples?handler=Add", ("name", "butter"), ("location", "Fridge"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var roux = await editor.AddRecipeAsync("Page Roux", "4 Tbsp butter\n1/2 cup all-purpose flour");
        var list = await NewListAsync(editor, "Gumbo");
        await editor.PostFormAsync("/shopping/new?handler=AddRecipe", ("slug", roux), ("scale", "1"), ("listId", list.Split('/')[^1]));

        using var phone = factory.CreateHttpsClient();
        var page = WebUtility.HtmlDecode(await phone.GetStringAsync(list, Ct));
        page.ShouldContain("Check before you buy");
        page.ShouldContain("(2 staples to check)");
        page.ShouldContain("Need it");
        page.ShouldNotContain("Check staples"); // the walk-through is for editors
        var keys = KeyField().Matches(page).Select(m => m.Groups[1].Value).Distinct().ToList();
        keys.Count.ShouldBe(2);

        var butterKey = keys.Single(k => page.Contains($"value=\"{k}\"", StringComparison.Ordinal) && page.IndexOf(k, StringComparison.Ordinal) > page.IndexOf(">butter<", StringComparison.Ordinal));
        (await phone.PostFormAsync(list + "?handler=Staple", [new("key", butterKey), new("answer", "Need")], tokenPage: list)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var flourKey = keys.Single(k => k != butterKey);
        (await phone.PostFormAsync(list + "?handler=Staple", [new("key", flourKey), new("answer", "Have")], tokenPage: list)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await phone.PostFormAsync(list + "?handler=Staple", [new("key", "f:999999"), new("answer", "Have")], tokenPage: list)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var laptop = factory.CreateHttpsClient();
        var after = WebUtility.HtmlDecode(await laptop.GetStringAsync(list, Ct));
        after.ShouldContain("Dairy and Eggs");
        after.ShouldContain("You have it.");
        after.ShouldNotContain("to check)");
    }

    [Fact]
    [Trait("Req", "AC-53")]
    [Trait("Req", "AC-125")]
    public async Task The_walk_through_restocks_by_location_and_the_staples_page_edits_the_list()
    {
        await using var factory = new RecipeWardenFactory();
        using var editor = await factory.SignInAsync("staple-walk@example.com");
        (await editor.PostFormAsync("/manage/staples?handler=Add", ("name", "eggs"), ("location", "Fridge"), ("restock", "1 dozen"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await editor.PostFormAsync("/manage/staples?handler=Add", ("name", "aluminum foil"), ("location", "Under the sink"), ("section", "Other")))
            .StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var bad = await editor.PostFormAsync("/manage/staples?handler=Add", ("name", "eggs"), ("location", "Fridge"));
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        WebUtility.HtmlDecode(await bad.TextAsync()).ShouldContain("That is already a staple.");

        var staples = WebUtility.HtmlDecode(await editor.GetStringAsync("/manage/staples", Ct));
        staples.ShouldContain("aluminum foil");
        staples.ShouldContain("(household item)");
        staples.IndexOf(">Fridge<", StringComparison.Ordinal).ShouldBeLessThan(staples.IndexOf(">Under the sink<", StringComparison.Ordinal));
        (await editor.GetStringAsync("/manage", Ct)).ShouldContain("/manage/staples");

        var list = await NewListAsync(editor, "Restock run", check: true);
        list.ShouldEndWith("/staples");
        var walk = WebUtility.HtmlDecode(await editor.GetStringAsync(list, Ct));
        walk.ShouldContain("Anything else running low?");
        walk.ShouldContain("egg");
        walk.ShouldContain("(1 dozen)");
        walk.IndexOf("<legend>Fridge</legend>", StringComparison.Ordinal).ShouldBeLessThan(walk.IndexOf("<legend>Under the sink</legend>", StringComparison.Ordinal));
        var eggKey = Regex.Match(walk, "name=\"restock\" value=\"(f:\\d+)\"").Groups[1].Value;

        (await editor.PostFormAsync(list, ("restock", eggKey))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var listPage = WebUtility.HtmlDecode(await editor.GetStringAsync(list[..^"/staples".Length], Ct));
        listPage.ShouldContain("<span class=\"item-name\">egg</span>");
        listPage.ShouldContain("<span class=\"item-amount\">12</span>");
        listPage.ShouldContain("for Restock");
        listPage.ShouldContain("Check staples");

        using var anon = factory.CreateHttpsClient();
        (await anon.GetAsync(list, Ct)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await editor.GetAsync("/shopping/999999/staples", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var foilId = Regex.Match(staples, "<th scope=\"row\">aluminum foil[\\s\\S]*?name=\"id\" value=\"(\\d+)\"").Groups[1].Value;
        foilId.ShouldNotBeEmpty();
        (await editor.PostFormAsync("/manage/staples?handler=Update", ("id", foilId), ("location", "Pantry"), ("restock", "1 roll"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await editor.PostFormAsync("/manage/staples?handler=Remove", ("id", foilId))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await editor.GetStringAsync("/manage/staples?done=removed", Ct)).ShouldNotContain("(household item)");
        (await editor.PostFormAsync("/manage/staples?handler=Remove", ("id", "999999"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
