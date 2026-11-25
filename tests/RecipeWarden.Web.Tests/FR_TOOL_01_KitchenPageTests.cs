using System.Net;

namespace RecipeWarden.Web.Tests;

/// <summary>FR-TOOL-01/02/03, BR-30, AC-40, W5, route /manage/kitchen.</summary>
[Trait("Req", "FR-TOOL-01")]
public sealed class FR_TOOL_01_KitchenPageTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    [Fact]
    public async Task Editors_see_the_standard_set_and_the_presets()
    {
        using var editor = await factory.SignInAsync("kitchen-view@example.com");
        var html = await factory.ReadAsync("/manage/kitchen", editor);
        foreach (var size in new[] { "¼ tsp", "½ tsp", "1 tsp", "1 Tbsp", "¼ cup", "⅓ cup", "½ cup", "1 cup" })
        {
            html.ShouldContain(size);
        }

        html.ShouldContain("Add: Digital scale (1 g / 0.05 oz)");
        html.ShouldContain("Inventory version");
    }

    [Fact]
    [Trait("Req", "AC-40")]
    [Trait("Req", "BR-30")]
    public async Task Adding_a_tool_increments_the_version_and_scaled_recipes_use_it()
    {
        using var editor = await factory.SignInAsync("kitchen-add@example.com");
        var slug = await editor.AddRecipeAsync("Half Spoon Cookies", "3/4 tsp vanilla extract");
        (await factory.ReadAsync($"/recipes/{slug}?scale=2")).ShouldContain("1½ tsp vanilla extract");

        var before = await factory.InventoryVersionAsync();
        (await editor.PostFormAsync("/manage/kitchen?handler=Add", ("Input.Type", "MeasuringSpoon"), ("Input.Size", "1/2 Tbsp"), ("Input.Quantity", "1")))
            .StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await factory.InventoryVersionAsync()).ShouldBe(before + 1);

        (await factory.ReadAsync($"/recipes/{slug}?scale=2")).ShouldContain("½ Tbsp vanilla extract");

        var id = await factory.ToolIdAsync("½ Tbsp");
        // BR-57: the only ½ Tbsp asks first, then retires when confirmed.
        var ask = await editor.PostFormAsync("/manage/kitchen?handler=Retire", ("id", id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        ask.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await ask.TextAsync()).ShouldContain("Retiring this leaves you without ½ Tbsp");
        (await editor.PostFormAsync("/manage/kitchen?handler=Retire", ("id", id.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("confirm", "true")))
            .StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await factory.ReadAsync($"/recipes/{slug}?scale=2")).ShouldContain("1½ tsp vanilla extract");
        (await factory.ReadAsync("/manage/kitchen", editor)).ShouldContain("Use again");
    }

    [Fact]
    public async Task Bad_input_is_refused_with_the_reason()
    {
        using var editor = await factory.SignInAsync("kitchen-bad@example.com");
        var before = await factory.InventoryVersionAsync();
        var response = await editor.PostFormAsync("/manage/kitchen?handler=Add", ("Input.Type", "Container"), ("Input.Name", "Jar"), ("Input.Size", "lots"));
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        WebUtility.HtmlDecode(await response.TextAsync()).ShouldContain("Enter the capacity with a volume unit");
        (await factory.InventoryVersionAsync()).ShouldBe(before);

        (await editor.PostFormAsync("/manage/kitchen?handler=Preset", ("key", "nope"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Req", "SEC-30")]
    public async Task Posts_without_a_token_are_rejected()
    {
        using var editor = await factory.SignInAsync("kitchen-csrf@example.com");
        var response = await editor.PostAsync("/manage/kitchen?handler=Preset",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["key"] = "scale" }), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Req", "SEC-22")]
    public async Task Tool_names_are_encoded()
    {
        using var editor = await factory.SignInAsync("kitchen-xss@example.com");
        await editor.PostFormAsync("/manage/kitchen?handler=Add", ("Input.Type", "Appliance"), ("Input.Name", "<script>alert(1)</script>"));
        var html = await editor.GetStringAsync("/manage/kitchen", TestContext.Current.CancellationToken);
        html.ShouldNotContain("<script>alert(1)</script>");
    }
}
