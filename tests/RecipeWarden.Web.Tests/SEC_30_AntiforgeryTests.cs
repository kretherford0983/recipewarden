using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Web.Tests;

/// <summary>AC-75, SEC-30: every unsafe handler added in M1 refuses a POST without a valid token (400) and changes nothing.</summary>
[Trait("Req", "AC-75")]
[Trait("Req", "SEC-30")]
public sealed class SEC_30_AntiforgeryTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    public static TheoryData<string> UnsafeHandlers() => new(
        "/manage/recipes/new",
        "/manage/recipes/new?handler=Parse",
        "/manage/recipes/1/edit",
        "/manage/recipes/1/edit?handler=Delete",
        "/manage/recipes/1/edit?handler=Parse",
        "/manage/trash?handler=Restore",
        "/manage/trash?handler=Purge",
        "/manage/tags?handler=Create",
        "/manage/tags?handler=Update",
        "/manage/tags?handler=Delete",
        "/manage/collections?handler=Create",
        "/manage/collections?handler=Update",
        "/manage/collections?handler=Delete",
        "/manage/collections?handler=Move",
        "/manage/collections?handler=AddRecipe",
        "/manage/collections?handler=MoveRecipe",
        "/manage/collections?handler=RemoveRecipe");

    [Theory]
    [MemberData(nameof(UnsafeHandlers))]
    public async Task Post_without_token_is_400_and_changes_nothing(string path)
    {
        await factory.SeedSamplesAsync();
        using var admin = await factory.SignInAsync("csrf-admin@example.com", AppRoles.Admin);
        var before = await SnapshotAsync();

        foreach (var token in new[] { null, "not-a-real-token" })
        {
            var fields = new Dictionary<string, string>
            {
                ["Input.Title"] = "CSRF", ["Input.Ingredients"] = "1 egg", ["Input.Steps"] = "Boil.", ["Action"] = "publish",
                ["id"] = "1", ["name"] = "csrf", ["collectionId"] = "1", ["recipeId"] = "1", ["direction"] = "1", ["version"] = "1",
            };
            if (token is not null)
            {
                fields["__RequestVerificationToken"] = token;
            }

            var response = await admin.PostAsync(path, new FormUrlEncodedContent(fields), TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, path);
        }

        (await SnapshotAsync()).ShouldBe(before);
    }

    private async Task<string> SnapshotAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ct = TestContext.Current.CancellationToken;
        return string.Join('|',
            await db.Recipes.IgnoreQueryFilters().CountAsync(ct),
            await db.Recipes.IgnoreQueryFilters().SumAsync(r => r.Version, ct),
            await db.Recipes.IgnoreQueryFilters().CountAsync(r => r.DeletedAt != null, ct),
            await db.Tags.CountAsync(ct),
            await db.Collections.CountAsync(ct),
            await db.Collections.SumAsync(c => c.SortOrder, ct),
            await db.CollectionRecipes.CountAsync(ct));
    }
}
