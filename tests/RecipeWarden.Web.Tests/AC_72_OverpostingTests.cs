using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Web.Tests;

/// <summary>
/// AC-72, SEC-13: forms bind to input DTOs, so extra fields (Id, Role, owner, audit, status, slug) are ignored
/// and nothing escalates. The last-Admin part of AC-72 arrives with /admin/users (M7).
/// </summary>
[Trait("Req", "AC-72")]
[Trait("Req", "SEC-13")]
public sealed class AC_72_OverpostingTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly (string, string)[] Extra =
    [
        ("Id", "4242"), ("Input.Id", "4242"), ("Input.Slug", "chosen-slug"), ("Slug", "chosen-slug"),
        ("Input.Status", "Published"), ("Status", "Published"), ("Input.CreatedById", "someone-else"),
        ("Input.UpdatedById", "someone-else"), ("Input.DeletedAt", "2020-01-01"), ("Input.Rating", "5"),
        ("Role", AppRoles.Admin), ("Input.Role", AppRoles.Admin), ("Roles", AppRoles.Admin),
    ];

    [Fact]
    public async Task Extra_fields_on_create_are_ignored()
    {
        using var editor = await factory.SignInAsync("over-create@example.com");

        var response = await editor.CreateRecipeAsync("Plain Draft", steps: "", action: "publish", extra: Extra);

        response.Headers.Location!.ToString().ShouldNotContain("4242");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var recipe = await db.Recipes.SingleAsync(r => r.Title == "Plain Draft", Ct);
        recipe.Id.ShouldNotBe(4242);
        recipe.Slug.ShouldBe("plain-draft");
        recipe.Status.ShouldBe(RecipeStatus.Draft);   // BR-01 still decides: no steps
        recipe.Rating.ShouldBeNull();
        recipe.DeletedAt.ShouldBeNull();
        recipe.CreatedById.ShouldNotBe("someone-else");

        await AssertNotAdminAsync("over-create@example.com");
    }

    [Fact]
    public async Task Extra_fields_on_edit_cannot_retarget_or_escalate()
    {
        using var editor = await factory.SignInAsync("over-edit@example.com");
        var mine = await editor.AddRecipeAsync("Mine");
        var other = await editor.AddRecipeAsync("Other");
        var mineId = await factory.RecipeIdAsync(mine);
        var otherId = await factory.RecipeIdAsync(other);

        var fields = new List<(string, string)>
        {
            ("Input.Title", "Mine Edited"), ("Input.Ingredients", "1 egg"), ("Input.Steps", "Boil."),
            ("Input.SourceType", "Manual"), ("Input.Version", "1"), ("Action", "publish"), ("id", otherId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        };
        fields.AddRange(Extra);
        var response = await editor.PostFormAsync($"/manage/recipes/{mineId}/edit", fields.ToArray());
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Recipes.SingleAsync(r => r.Id == mineId, Ct)).Title.ShouldBe("Mine Edited");
        (await db.Recipes.SingleAsync(r => r.Id == otherId, Ct)).Title.ShouldBe("Other"); // the route id wins
        await AssertNotAdminAsync("over-edit@example.com");
    }

    [Fact]
    public async Task Extra_fields_on_collection_and_tag_forms_are_ignored()
    {
        using var editor = await factory.SignInAsync("over-org@example.com");
        (await editor.PostFormAsync("/manage/collections?handler=Create", ("name", "Over List"), ("Version", "99"), ("SortOrder", "-5"),
            ("Id", "777"), ("Role", AppRoles.Admin))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await editor.PostFormAsync("/manage/tags?handler=Create", ("name", "overtag"), ("NormalizedName", "x"), ("Slug", "../x"),
            ("Id", "777"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var c = await db.Collections.SingleAsync(x => x.Name == "Over List", Ct);
        c.Id.ShouldNotBe(777);
        c.Version.ShouldBe(1);
        c.SortOrder.ShouldBeGreaterThanOrEqualTo(0);
        var t = await db.Tags.SingleAsync(x => x.Name == "overtag", Ct);
        t.Slug.ShouldBe("overtag");
        t.NormalizedName.ShouldBe("overtag");
        await AssertNotAdminAsync("over-org@example.com");
    }

    private async Task AssertNotAdminAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = (await users.FindByEmailAsync(email)).ShouldNotBeNull();
        (await users.IsInRoleAsync(user, AppRoles.Admin)).ShouldBeFalse();
    }
}
