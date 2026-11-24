using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Web.Tests;

/// <summary>BR-120, BR-121, AC-113, AC-114: /manage/kitchen shows and keeps only the fields of the chosen type.</summary>
[Trait("Req", "FR-TOOL-01")]
[Trait("Req", "BR-120")]
public sealed class BR_120_KitchenFormPageTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Req", "AC-113")]
    public async Task The_add_form_opens_with_only_the_spoon_fields()
    {
        using var editor = await factory.SignInAsync("kitchen-form-view@example.com");
        var html = await editor.GetStringAsync("/manage/kitchen", Ct);
        html.ShouldContain("data-kitchen-part=\"units\" hidden");
        html.ShouldContain("data-kitchen-part=\"fill\" hidden");
        html.ShouldNotContain("data-kitchen-part=\"size\" hidden");
        html.ShouldContain("/js/kitchen.js");
        WebUtility.HtmlDecode(html).ShouldContain("Those modes assume the food is water");
    }

    [Fact]
    [Trait("Req", "AC-113")]
    public async Task A_failed_scale_post_shows_the_scale_fields()
    {
        using var editor = await factory.SignInAsync("kitchen-form-scale@example.com");
        var response = await editor.PostFormAsync("/manage/kitchen?handler=Add", ("Input.Type", "Scale"), ("Input.Name", "Bad scale"),
            ("Input.Size", "60 g"), ("Input.Shows", "g"));
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var html = await response.TextAsync();
        html.ShouldNotContain("data-kitchen-part=\"units\" hidden");
        html.ShouldContain("data-kitchen-part=\"fill\" hidden");
        WebUtility.HtmlDecode(html).ShouldContain("Resolution (smallest step)");
        WebUtility.HtmlDecode(html).ShouldContain("at most 50 g");
    }

    [Fact]
    [Trait("Req", "AC-113")]
    public async Task Fields_from_another_type_are_not_stored()
    {
        using var editor = await factory.SignInAsync("kitchen-form-foreign@example.com");
        (await editor.PostFormAsync("/manage/kitchen?handler=Add", ("Input.Type", "MeasuringSpoon"), ("Input.Name", "Foreign spoon"),
            ("Input.Size", "1/8 tsp"), ("Input.Shows", "g"), ("Input.FillPct", "40"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        await using var scope = factory.Services.CreateAsyncScope();
        var tool = await scope.ServiceProvider.GetRequiredService<AppDbContext>().KitchenTools.SingleAsync(t => t.Name == "Foreign spoon", Ct);
        tool.DefaultFillPct.ShouldBe(100m);
        tool.SupportedUnits.ShouldBeNullOrEmpty();
        tool.ResolutionAmount.ShouldBeNull();
    }

    [Fact]
    [Trait("Req", "AC-114")]
    [Trait("Req", "BR-121")]
    public async Task A_bare_number_is_a_step_in_grams_and_the_edit_form_shows_scale_fields_only()
    {
        using var editor = await factory.SignInAsync("kitchen-form-grams@example.com");
        (await editor.PostFormAsync("/manage/kitchen?handler=Add", ("Input.Type", "Scale"), ("Input.Name", "Label scale"),
            ("Input.Size", "1"), ("Input.Shows", "g"), ("Input.Shows", "oz"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var id = await factory.ToolIdAsync("Label scale");
        var html = WebUtility.HtmlDecode(await editor.GetStringAsync("/manage/kitchen", Ct));
        html.ShouldContain("1 g steps; shows g, oz");
        html.ShouldContain($"for=\"size-{id}\">Resolution (smallest step)");
        html.ShouldNotContain($"id=\"fill-{id}\"");
    }
}
