using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Web.Pages.Manage.Recipes;

namespace RecipeWarden.Web.Tests;

/// <summary>ADR 0016, FR-ORG-01, NFR-03, UX-13: tags picked in a dropdown of checkbox groups by type, plus a box for new tags.</summary>
[Trait("Req", "FR-ORG-01")]
[Trait("Req", "UX-13")]
public sealed partial class ADR_0016_TagPickerTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<int> TagIdAsync(string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Tags.Where(t => t.Name == name).Select(t => t.Id).SingleAsync(Ct);
    }

    private async Task<string[]> RecipeTagsAsync(string slug)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = await db.Recipes.Where(r => r.Slug == slug).Select(r => r.Id).SingleAsync(Ct);
        return await db.RecipeTags.Where(rt => rt.RecipeId == id).Select(rt => rt.Tag!.Name).OrderBy(n => n).ToArrayAsync(Ct);
    }

    [Fact]
    public async Task The_editor_shows_tags_grouped_by_type_as_checkboxes()
    {
        using var editor = await factory.SignInAsync("picker-view@example.com");
        var html = WebUtility.HtmlDecode(await editor.GetStringAsync("/manage/recipes/new", Ct));

        html.ShouldContain("<legend>Tags</legend>");
        foreach (var group in new[] { "Meal", "Course", "Season", "Method", "Diet" })
        {
            html.ShouldContain($"<p class=\"tag-group-label\">{group}</p>"); // UX-13: groups inside the dropdown
        }

        html.ShouldContain($"name=\"Input.TagIds\" value=\"{await TagIdAsync("Dinner")}\"");
        html.ShouldContain("New tags");
    }

    [Fact]
    public async Task Ticked_tags_and_new_names_are_saved_and_typed_existing_names_select_the_tag()
    {
        using var editor = await factory.SignInAsync("picker-save@example.com");
        var dinner = await TagIdAsync("Dinner");
        var soup = await TagIdAsync("Soup");
        var response = await editor.CreateRecipeAsync("Picker Soup", tags: "slow cooker, Weeknight Favourite",
            extra: [("Input.TagIds", dinner.ToString(CultureInfo.InvariantCulture)), ("Input.TagIds", soup.ToString(CultureInfo.InvariantCulture))]);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect, await response.TextAsync());
        var slug = response.Headers.Location!.ToString()["/recipes/".Length..];

        (await RecipeTagsAsync(slug)).ShouldBe(["Dinner", "Slow Cooker", "Soup", "Weeknight Favourite"]);

        var id = await factory.RecipeIdAsync(slug);
        var edit = WebUtility.HtmlDecode(await editor.GetStringAsync($"/manage/recipes/{id}/edit", Ct));
        CheckedIds(edit).ShouldBe(new[] { dinner, soup, await TagIdAsync("Slow Cooker"), await TagIdAsync("Weeknight Favourite") }.Order().ToArray());
        edit.ShouldContain("<span class=\"chip\">Dinner</span>"); // UX-13: the closed dropdown shows the ticked tags
        Regex.Match(edit, "id=\"Input_Tags\"[^>]*value=\"([^\"]*)\"").Groups[1].Value.ShouldBe(string.Empty);
    }

    [Fact]
    [Trait("Req", "SEC-13")]
    public async Task An_unknown_tag_id_is_refused_not_ignored()
    {
        using var editor = await factory.SignInAsync("picker-bad@example.com");
        var response = await editor.CreateRecipeAsync("Picker Bad", extra: [("Input.TagIds", "987654")]);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await response.TextAsync()).ShouldContain(RecipeEditorPageModel.UnknownTag);
    }

    [GeneratedRegex("""<input type="checkbox" name="Input.TagIds" value="(\d+)" checked""")]
    private static partial Regex CheckedBox();

    private static int[] CheckedIds(string html) =>
        CheckedBox().Matches(html).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Order().ToArray();
}
