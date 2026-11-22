using System.Globalization;
using System.Net;

namespace RecipeWarden.Web.Tests;

/// <summary>FR-SCL-04/05/06, BR-111, BR-114, AC-37, AC-38, AC-39, ADR 0019; FR-MEAS-07 pans (ADR 0020).</summary>
[Trait("Req", "FR-SCL-04")]
public sealed class FR_SCL_04_ContainerPageTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private async Task<(HttpClient Editor, string Slug, int Bottle)> SauceAsync(string title, string? yieldText)
    {
        var editor = await factory.SignInAsync("sauce-editor@example.com");
        await editor.PostFormAsync("/manage/kitchen?handler=Preset", ("key", "squeeze-bottle"));
        var extra = yieldText is null ? Array.Empty<(string, string)>() : [("Input.Yield", yieldText)];
        var response = await editor.CreateRecipeAsync(title, "1 cup ketchup\n2 tbsp cider vinegar\n2 cloves garlic", extra: extra);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect, await response.TextAsync());
        return (editor, response.Headers.Location!.ToString()["/recipes/".Length..], await factory.ToolIdAsync("16 oz squeeze bottle"));
    }

    private static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);

    [Fact]
    [Trait("Req", "AC-37")]
    public async Task A_12_fl_oz_sauce_fits_a_16_oz_bottle_at_1_2x_and_three_bottles_at_3_6x()
    {
        var (editor, slug, bottle) = await SauceAsync("Squeeze Sauce", "12 fl oz");
        using var _ = editor;

        var one = await factory.ReadAsync($"/recipes/{slug}?fit={Id(bottle)}");
        one.ShouldContain("Scaled 1.2x (fit to bottle)");
        one.ShouldContain("fills about 14.4 of 16 fl oz");

        var three = await factory.ReadAsync($"/recipes/{slug}?fit={Id(bottle)}&count=3");
        three.ShouldContain("Scaled 3.6x (fit to 3 bottles)");
        three.ShouldContain("You have 1; this needs 3."); // BR-114
    }

    [Fact]
    [Trait("Req", "AC-38")]
    public async Task At_2x_it_makes_one_bottle_and_two_thirds_of_another()
    {
        var (editor, slug, bottle) = await SauceAsync("Howmany Sauce", "12 fl oz");
        using var _ = editor;
        (await factory.ReadAsync($"/recipes/{slug}?scale=2&howmany={Id(bottle)}")).ShouldContain("This makes 1 full bottle + about ⅔ of another.");
    }

    [Fact]
    [Trait("Req", "AC-39")]
    public async Task Without_a_yield_the_estimate_is_labelled_and_a_measured_yield_wins_later()
    {
        var (editor, slug, bottle) = await SauceAsync("Estimate Sauce", null);
        using var _ = editor;

        var estimated = await factory.ReadAsync($"/recipes/{slug}?fit={Id(bottle)}");
        estimated.ShouldContain("The yield is an estimate; not counted: garlic");

        var typed = await factory.ReadAsync($"/recipes/{slug}?fit={Id(bottle)}&yield=12+fl+oz");
        typed.ShouldContain("Scaled 1.2x");

        var id = await factory.RecipeIdAsync(slug);
        (await factory.ReadAsync($"/manage/recipes/{id}/cooked", editor)).ShouldContain("How much did it make?");
        var made = await editor.PostFormAsync($"/manage/recipes/{id}/cooked", ("Made", "8 fl oz"));
        made.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        made.Headers.Location!.ToString().ShouldBe($"/recipes/{slug}?made=1");

        var measured = await factory.ReadAsync($"/recipes/{slug}?fit={Id(bottle)}");
        measured.ShouldContain("Scaled 1.8x"); // 14.4 ÷ 8
        measured.ShouldNotContain("The yield is an estimate");

        (await editor.PostFormAsync($"/manage/recipes/{id}/cooked", ("Made", "loads"))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_typed_capacity_works_without_my_kitchen()
    {
        var (editor, slug, _) = await SauceAsync("Typed Sauce", "1 cup");
        using var __ = editor;
        (await factory.ReadAsync($"/recipes/{slug}?capacity=1+quart&fill=100")).ShouldContain("Scaled 4x (fit to container)");
        (await factory.ReadAsync($"/recipes/{slug}?capacity=lots")).ShouldContain("Choose a container from My Kitchen or type a capacity");
    }

    [Fact]
    [Trait("Req", "FR-MEAS-07")]
    public async Task Doubling_a_9x13_recipe_suggests_pans()
    {
        using var editor = await factory.SignInAsync("pan-editor@example.com");
        var response = await editor.CreateRecipeAsync("Pan Brownies", "1 cup flour", "Pour into a greased 9x13-inch pan.\nBake 25 minutes.");
        var slug = response.Headers.Location!.ToString()["/recipes/".Length..];

        var html = await factory.ReadAsync($"/recipes/{slug}?scale=2");
        html.ShouldContain("Pan: 9x13 (from the steps). At 2x, these hold about the same:");
        html.ShouldContain("half sheet (13x18)");
        html.ShouldContain("A different pan changes the bake time");

        var id = await factory.RecipeIdAsync(slug);
        var form = await factory.ReadAsync($"/manage/recipes/{id}/edit", editor);
        form.ShouldContain("value=\"9x13\"");
        form.ShouldContain("Found in the steps");
    }

    [Fact]
    public async Task Editor_yield_and_pan_fields_round_trip_and_bad_values_are_refused()
    {
        using var editor = await factory.SignInAsync("yield-editor@example.com");
        var response = await editor.CreateRecipeAsync("Field Cake", extra: [("Input.Yield", "2 quarts"), ("Input.Pan", "9-inch round"), ("Input.FinishedDensity", "1.1")]);
        var slug = response.Headers.Location!.ToString()["/recipes/".Length..];
        var form = await factory.ReadAsync($"/manage/recipes/{await factory.RecipeIdAsync(slug)}/edit", editor);
        form.ShouldContain("value=\"2 quarts\"");
        form.ShouldContain("value=\"9-inch round\"");
        form.ShouldContain("value=\"1.1\"");

        var bad = await editor.CreateRecipeAsync("Bad Field Cake", extra: [("Input.Yield", "3 jars"), ("Input.FinishedDensity", "heavy")]);
        bad.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await bad.TextAsync());
        html.ShouldContain("Enter the density as a number of g/ml");
    }
}
