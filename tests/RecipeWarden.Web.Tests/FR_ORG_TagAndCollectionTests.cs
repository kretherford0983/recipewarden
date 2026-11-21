using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Web.Tests;

/// <summary>FR-ORG-01/02, AC-20 over HTTP: one tag per name; collections created, reordered and public.</summary>
[Trait("Req", "AC-20")]
public sealed class FR_ORG_TagAndCollectionTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Req", "FR-ORG-01")]
    public async Task Dinner_and_dinner_from_two_recipes_are_one_public_tag()
    {
        using var editor = await factory.SignInAsync("tags-1@example.com");
        await editor.AddRecipeAsync("Tag Soup", tags: "Dinner");
        await editor.AddRecipeAsync("Tag Stew", tags: "dinner, DINNER");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Tags.CountAsync(t => t.NormalizedName == "dinner", Ct)).ShouldBe(1);
        }

        using var reader = factory.CreateHttpsClient();
        var page = await reader.GetStringAsync("/tags/dinner", Ct);
        page.ShouldContain("Tag Soup");
        page.ShouldContain("Tag Stew");
        (await reader.GetAsync("/tags/no-such-tag", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Req", "FR-ORG-01")]
    public async Task Tags_can_be_renamed_typed_and_deleted()
    {
        using var editor = await factory.SignInAsync("tags-2@example.com");
        (await editor.PostFormAsync("/manage/tags?handler=Create", ("name", "Tex Mex"), ("type", "Cuisine"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await editor.PostFormAsync("/manage/tags?handler=Create", ("name", "tex  mex"))).StatusCode.ShouldBe(HttpStatusCode.OK);

        int id;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            id = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Tags.SingleAsync(t => t.Slug == "tex-mex", Ct)).Id;
        }

        var idText = id.ToString(CultureInfo.InvariantCulture);
        (await editor.PostFormAsync("/manage/tags?handler=Update", ("id", idText), ("name", "Southwestern"), ("type", "Cuisine")))
            .Headers.Location!.ToString().ShouldBe("/manage/tags?done=saved");
        (await editor.GetStringAsync("/manage/tags", Ct)).ShouldContain("value=\"Southwestern\"");
        (await editor.PostFormAsync("/manage/tags?handler=Delete", ("id", idText))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await editor.GetStringAsync("/manage/tags", Ct)).ShouldNotContain("Southwestern");
        (await editor.PostFormAsync("/manage/tags?handler=Update", ("id", "999999"), ("name", "x"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Req", "FR-ORG-02")]
    public async Task Collections_are_created_filled_reordered_and_visible_to_anonymous_readers()
    {
        using var editor = await factory.SignInAsync("coll-1@example.com");
        var soup = await editor.AddRecipeAsync("List Soup");
        var stew = await editor.AddRecipeAsync("List Stew");
        (await editor.PostFormAsync("/manage/collections?handler=Create", ("name", "Winter Warmers"), ("description", "Cosy *bowls*.")))
            .Headers.Location!.ToString().ShouldBe("/manage/collections?c=winter-warmers&done=saved");
        (await editor.PostFormAsync("/manage/collections?handler=Create", ("name", "Summer")))
            .StatusCode.ShouldBe(HttpStatusCode.Redirect);

        int collectionId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            collectionId = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Collections.SingleAsync(c => c.Slug == "winter-warmers", Ct)).Id;
        }

        var cid = collectionId.ToString(CultureInfo.InvariantCulture);
        foreach (var slug in new[] { soup, stew })
        {
            var recipeId = (await factory.RecipeIdAsync(slug)).ToString(CultureInfo.InvariantCulture);
            var added = await editor.PostFormAsync("/manage/collections?handler=AddRecipe", ("collectionId", cid), ("recipeId", recipeId), ("returnSlug", slug));
            added.Headers.Location!.ToString().ShouldBe("/recipes/" + slug);
        }

        var stewId = (await factory.RecipeIdAsync(stew)).ToString(CultureInfo.InvariantCulture);
        await editor.PostFormAsync("/manage/collections?handler=MoveRecipe", ("id", cid), ("recipeId", stewId), ("direction", "-1"));
        await editor.PostFormAsync("/manage/collections?handler=Move", ("id", cid), ("direction", "1"));

        using var reader = factory.CreateHttpsClient();
        var index = await reader.GetStringAsync("/collections", Ct);
        index.IndexOf("Summer", StringComparison.Ordinal).ShouldBeLessThan(index.IndexOf("Winter Warmers", StringComparison.Ordinal));
        var detail = await reader.GetStringAsync("/collections/winter-warmers", Ct);
        detail.IndexOf("List Stew", StringComparison.Ordinal).ShouldBeLessThan(detail.IndexOf("List Soup", StringComparison.Ordinal));
        detail.ShouldContain("<em>bowls</em>");
        detail.ShouldNotContain("Edit this collection"); // UX-11
        (await reader.GetStringAsync("/recipes/" + soup, Ct)).ShouldContain("href=\"/collections/winter-warmers\"");
        (await reader.GetAsync("/collections/nope", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Req", "FR-ORG-02")]
    public async Task Collection_rename_remove_and_delete()
    {
        using var editor = await factory.SignInAsync("coll-2@example.com");
        var slug = await editor.AddRecipeAsync("Lonely Loaf");
        await editor.PostFormAsync("/manage/collections?handler=Create", ("name", "Breads"));
        int id;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            id = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Collections.SingleAsync(c => c.Slug == "breads", Ct)).Id;
        }

        var cid = id.ToString(CultureInfo.InvariantCulture);
        var rid = (await factory.RecipeIdAsync(slug)).ToString(CultureInfo.InvariantCulture);
        await editor.PostFormAsync("/manage/collections?handler=AddRecipe", ("collectionId", cid), ("recipeId", rid), ("returnSlug", "https://evil.example"));

        (await editor.PostFormAsync("/manage/collections?handler=Update", ("id", cid), ("name", "Loaves"), ("version", "1")))
            .Headers.Location!.ToString().ShouldBe("/manage/collections?c=loaves&done=saved");
        (await editor.PostFormAsync("/manage/collections?handler=Update", ("id", cid), ("name", "Stale"), ("version", "1")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await editor.GetStringAsync("/manage/collections?c=loaves", Ct)).ShouldContain("Lonely Loaf");

        await editor.PostFormAsync("/manage/collections?handler=RemoveRecipe", ("id", cid), ("recipeId", rid));
        (await editor.GetStringAsync("/manage/collections?c=loaves", Ct)).ShouldNotContain("Lonely Loaf");

        await editor.PostFormAsync("/manage/collections?handler=Delete", ("id", cid));
        (await editor.GetAsync("/manage/collections?c=loaves", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await editor.PostFormAsync("/manage/collections?handler=AddRecipe", ("collectionId", cid), ("recipeId", rid)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Add_to_collection_never_redirects_off_site()
    {
        using var editor = await factory.SignInAsync("coll-3@example.com");
        var slug = await editor.AddRecipeAsync("Redirect Roll");
        await editor.PostFormAsync("/manage/collections?handler=Create", ("name", "Rolls"));
        int id;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            id = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Collections.SingleAsync(c => c.Slug == "rolls", Ct)).Id;
        }

        var response = await editor.PostFormAsync("/manage/collections?handler=AddRecipe",
            ("collectionId", id.ToString(CultureInfo.InvariantCulture)),
            ("recipeId", (await factory.RecipeIdAsync(slug)).ToString(CultureInfo.InvariantCulture)),
            ("returnSlug", "//evil.example/x"));
        response.Headers.Location!.ToString().ShouldBe("/manage/collections?done=added");
    }
}
