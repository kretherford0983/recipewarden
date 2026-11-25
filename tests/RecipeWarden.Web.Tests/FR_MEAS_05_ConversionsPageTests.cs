using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Web.Tests;

/// <summary>FR-MEAS-05, route /manage/conversions: editors view and edit densities, vague amounts and yields.</summary>
[Trait("Req", "FR-MEAS-05")]
public sealed class FR_MEAS_05_ConversionsPageTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<int> IdAsync(Func<AppDbContext, Task<int>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public async Task Editors_see_the_seeded_foods_vague_amounts_and_yields()
    {
        using var editor = await factory.SignInAsync("conv-view@example.com");
        var html = WebUtility.HtmlDecode(await editor.GetStringAsync("/manage/conversions", Ct));

        html.ShouldContain("all-purpose flour");
        html.ShouldContain("value=\"0.5283\"");
        html.ShouldContain("pinch");
        html.ShouldContain("cooked chicken breast → raw");
        html.ShouldContain("USDA FoodData Central");

        var filtered = WebUtility.HtmlDecode(await editor.GetStringAsync("/manage/conversions?q=ap+flour", Ct));
        filtered.ShouldContain("all-purpose flour");
        filtered.ShouldNotContain("granulated sugar");
    }

    [Fact]
    public async Task Density_edit_is_saved_and_a_typo_is_refused()
    {
        using var editor = await factory.SignInAsync("conv-edit@example.com");
        var id = await IdAsync(db => db.Foods.Where(f => f.Name == "bread flour").Select(f => f.Id).SingleAsync(Ct));

        var ok = await editor.PostFormAsync("/manage/conversions?handler=UpdateFood", ("id", id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("density", "0.54"), ("aliases", "strong flour"));
        ok.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var bad = await editor.PostFormAsync("/manage/conversions?handler=UpdateFood", ("id", id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("density", "54"), ("aliases", ""));
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        WebUtility.HtmlDecode(await bad.TextAsync()).ShouldContain(FoodRules.ValidateDensity(54m)!);

        var nan = await editor.PostFormAsync("/manage/conversions?handler=UpdateFood", ("id", "1"), ("density", "lots"));
        nan.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        await using var scope = factory.Services.CreateAsyncScope();
        var food = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Foods.Include(f => f.Aliases).AsNoTracking().SingleAsync(f => f.Id == id, Ct);
        food.DensityGPerMl.ShouldBe(0.54m);
        food.Aliases.Select(a => a.Alias).ShouldBe(["strong flour"]);
    }

    [Fact]
    public async Task Yield_is_entered_as_percentages_and_vague_amounts_in_teaspoons()
    {
        using var editor = await factory.SignInAsync("conv-yield@example.com");
        var yieldId = await IdAsync(db => db.YieldConversions.Where(y => y.Food!.Name == "pork tenderloin").Select(y => y.Id).SingleAsync(Ct));
        var dashId = await IdAsync(db => db.Units.Where(u => u.Code == "dash").Select(u => u.Id).SingleAsync(Ct));

        (await editor.PostFormAsync("/manage/conversions?handler=UpdateYield", ("id", yieldId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("lowPct", "80"), ("highPct", "100"), ("density", ""), ("basis", "household roast"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await editor.PostFormAsync("/manage/conversions?handler=UpdateVague", ("id", dashId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("teaspoons", "0.25"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await editor.PostFormAsync("/manage/conversions?handler=UpdateVague", ("id", dashId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("teaspoons", "10"))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.YieldConversions.AsNoTracking().SingleAsync(y => y.Id == yieldId, Ct);
        (row.Ratio, row.RatioMax, row.Basis).ShouldBe((1m, 1.25m, "household roast"));
        (await db.Units.AsNoTracking().SingleAsync(u => u.Id == dashId, Ct)).ToBase.ShouldBe(UnitCatalog.FindByCode("tsp")!.ToBase / 4m, 0.0000001m);
    }

    [Fact]
    public async Task A_household_food_can_be_added_and_deleted()
    {
        using var editor = await factory.SignInAsync("conv-add@example.com");
        (await editor.PostFormAsync("/manage/conversions?handler=AddFood", ("name", "gochujang"), ("density", "1.2"), ("aliases", "korean chili paste")))
            .StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await editor.PostFormAsync("/manage/conversions?handler=AddFood", ("name", "Gochujang"), ("density", ""), ("aliases", "")))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var id = await IdAsync(db => db.Foods.Where(f => f.Name == "gochujang").Select(f => f.Id).SingleAsync(Ct));
        (await editor.PostFormAsync("/manage/conversions?handler=DeleteFood", ("id", id.ToString(System.Globalization.CultureInfo.InvariantCulture))))
            .StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await IdAsync(db => db.Foods.CountAsync(f => f.Name == "gochujang", Ct))).ShouldBe(0);
    }

    [Fact]
    [Trait("Req", "SEC-30")]
    public async Task Posts_without_a_token_are_rejected()
    {
        using var editor = await factory.SignInAsync("conv-csrf@example.com");
        var response = await editor.PostAsync("/manage/conversions?handler=AddFood",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["name"] = "x" }), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Names_and_notes_are_encoded()
    {
        using var editor = await factory.SignInAsync("conv-xss@example.com");
        (await editor.PostFormAsync("/manage/conversions?handler=AddFood", ("name", "<script>alert(1)</script>"), ("density", "")))
            .StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var html = await editor.GetStringAsync("/manage/conversions", Ct);
        html.ShouldNotContain("<script>alert(1)</script>");
        html.ShouldContain("&lt;script&gt;");
    }
}
