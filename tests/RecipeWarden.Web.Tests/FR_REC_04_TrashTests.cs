using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Web.Hosting;

namespace RecipeWarden.Web.Tests;

/// <summary>FR-REC-04, BR-05, AC-04, UX-07, SEC-14: trash with undo, restore, and Admin-only purge.</summary>
[Trait("Req", "FR-REC-04")]
[Trait("Req", "BR-05")]
[Trait("Req", "AC-04")]
public sealed class FR_REC_04_TrashTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Req", "UX-07")]
    public async Task Delete_hides_it_lists_it_in_the_trash_and_offers_undo()
    {
        using var editor = await factory.SignInAsync("trash-1@example.com");
        var slug = await editor.AddRecipeAsync("Doomed Dip");
        var id = await factory.RecipeIdAsync(slug);

        var deleted = await editor.PostFormAsync($"/manage/recipes/{id}/edit?handler=Delete");
        deleted.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        deleted.Headers.Location!.ToString().ShouldBe($"/manage/trash?deleted={id}");

        using var reader = factory.CreateHttpsClient();
        (await reader.GetAsync("/recipes/" + slug, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await editor.GetAsync("/recipes/" + slug, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var trash = await editor.GetStringAsync($"/manage/trash?deleted={id}", Ct);
        trash.ShouldContain("Moved to the trash.");
        trash.ShouldContain("Doomed Dip");
        trash.ShouldContain(">Undo</button>");

        var undo = await editor.PostFormAsync("/manage/trash?handler=Restore", ("id", id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        undo.Headers.Location!.ToString().ShouldBe("/manage/trash?done=restored");
        (await reader.GetAsync("/recipes/" + slug, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Req", "SEC-14")]
    public async Task Editor_cannot_purge_even_by_posting_directly_but_admin_can()
    {
        using var editor = await factory.SignInAsync("trash-2@example.com");
        var slug = await editor.AddRecipeAsync("Purge Me");
        var id = await factory.RecipeIdAsync(slug);
        var idText = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await editor.PostFormAsync($"/manage/recipes/{id}/edit?handler=Delete");

        (await editor.GetStringAsync("/manage/trash", Ct)).ShouldNotContain("Delete for good");
        (await editor.PostFormAsync("/manage/trash?handler=Purge", ("id", idText))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CountAsync(id)).ShouldBe(1);

        using var admin = await factory.SignInAsync("trash-admin@example.com", AppRoles.Admin);
        (await admin.GetStringAsync("/manage/trash", Ct)).ShouldContain("Delete for good");
        (await admin.PostFormAsync("/manage/trash?handler=Purge", ("id", idText))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await CountAsync(id)).ShouldBe(0);
    }

    [Fact]
    public async Task After_30_days_restore_fails_and_the_purge_job_removes_it()
    {
        using var editor = await factory.SignInAsync("trash-3@example.com");
        var slug = await editor.AddRecipeAsync("Old Leftovers");
        var id = await factory.RecipeIdAsync(slug);
        await editor.PostFormAsync($"/manage/recipes/{id}/edit?handler=Delete");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var old = DateTimeOffset.UtcNow.AddDays(-31);
            await db.Recipes.IgnoreQueryFilters().Where(r => r.Id == id).ExecuteUpdateAsync(u => u.SetProperty(r => r.DeletedAt, old), Ct);
        }

        var restore = await editor.PostFormAsync("/manage/trash?handler=Restore", ("id", id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        restore.Headers.Location!.ToString().ShouldBe("/manage/trash?done=expired");

        var job = factory.Services.GetServices<IHostedService>().OfType<TrashPurgeService>().Single();
        (await job.RunOnceAsync(Ct)).ShouldBeGreaterThanOrEqualTo(1);
        (await CountAsync(id)).ShouldBe(0);
    }

    private async Task<int> CountAsync(int id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Recipes.IgnoreQueryFilters().CountAsync(r => r.Id == id, Ct);
    }
}
