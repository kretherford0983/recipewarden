using System.Globalization;
using System.Net;

namespace RecipeWarden.Web.Tests;

/// <summary>
/// BR-56, BR-57, AC-135, AC-136: a jug with markings on /manage/kitchen, recipes using a marking as one measure, and
/// retiring the only tool of a size. Own factory per test: the kitchen is shared by the whole site.
/// </summary>
[Trait("Req", "FR-TOOL-01")]
[Trait("Req", "BR-56")]
public sealed class BR_56_GraduatedMeasurePageTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Req", "AC-135")]
    public async Task Markings_are_saved_and_an_adjustable_cup_makes_five_eighths_one_measure()
    {
        await using var factory = new RecipeWardenFactory();
        using var editor = await factory.SignInAsync("marks-jug@example.com");
        await editor.PostFormAsync("/manage/kitchen?handler=Preset", ("key", "standard"));
        var slug = await editor.AddRecipeAsync("Jug Soup", "5/16 cup stock");
        (await factory.ReadAsync($"/recipes/{slug}?scale=2")).ShouldContain("½ cup + 2 Tbsp stock");

        var page = WebUtility.HtmlDecode(await editor.GetStringAsync("/manage/kitchen", Ct));
        page.ShouldContain("One of each size is enough.");
        page.ShouldContain("data-markings=\"True\"");

        (await editor.PostFormAsync("/manage/kitchen?handler=Add", ("Input.Type", "LiquidMeasure"), ("Input.Name", "Pyrex"), ("Input.Size", "4 cups"),
            ("Input.Markings", "4 cups by 1/2\n1 quart by 1/3\n1000 ml by 100"), ("Input.Quantity", "1"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var listed = WebUtility.HtmlDecode(await editor.GetStringAsync("/manage/kitchen", Ct));
        listed.ShouldContain("4 cups; marked 4 cups by ½, 1 quart by ⅓, 1000 ml by 100");

        (await editor.PostFormAsync("/manage/kitchen?handler=Add", ("Input.Type", "MeasuringCup"), ("Input.Name", "Adjust-A-Cup"), ("Input.Size", "1 cup"),
            ("Input.Markings", "1/8 to 1 cup by 1/8"), ("Input.Quantity", "1"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await factory.ReadAsync($"/recipes/{slug}?scale=2")).ShouldContain("⅝ cup stock");

        var bad = await editor.PostFormAsync("/manage/kitchen?handler=Add", ("Input.Type", "LiquidMeasure"), ("Input.Name", "Odd jug"), ("Input.Size", "2 cups"),
            ("Input.Markings", "lots of lines"));
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        WebUtility.HtmlDecode(await bad.TextAsync()).ShouldContain("Couldn't read \"lots of lines\"");
    }

    [Fact]
    [Trait("Req", "AC-136")]
    [Trait("Req", "BR-57")]
    public async Task Retiring_asks_when_a_size_would_be_lost_and_says_when_another_tool_covers_it()
    {
        await using var factory = new RecipeWardenFactory();
        using var editor = await factory.SignInAsync("marks-retire@example.com");
        await editor.PostFormAsync("/manage/kitchen?handler=Preset", ("key", "standard"));
        (await editor.PostFormAsync("/manage/kitchen?handler=Add", ("Input.Type", "LiquidMeasure"), ("Input.Name", "Pyrex"), ("Input.Size", "4 cups"),
            ("Input.Markings", "4 cups by 1/2"), ("Input.Quantity", "1"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var third = (await factory.ToolIdAsync("⅓ cup")).ToString(CultureInfo.InvariantCulture);
        var ask = await editor.PostFormAsync("/manage/kitchen?handler=Retire", ("id", third));
        ask.StatusCode.ShouldBe(HttpStatusCode.OK);
        var askText = WebUtility.HtmlDecode(await ask.TextAsync());
        askText.ShouldContain("Retiring this leaves you without ⅓ cup.");
        askText.ShouldContain("Retire anyway");

        var cup = (await factory.ToolIdAsync("1 cup")).ToString(CultureInfo.InvariantCulture);
        var retired = await editor.PostFormAsync("/manage/kitchen?handler=Retire", ("id", cup));
        retired.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var after = WebUtility.HtmlDecode(await editor.GetStringAsync(retired.Headers.Location!.OriginalString, Ct));
        after.ShouldContain("Retired. 1 cup is still covered by Pyrex.");

        (await editor.PostFormAsync("/manage/kitchen?handler=Retire", ("id", "999999"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
