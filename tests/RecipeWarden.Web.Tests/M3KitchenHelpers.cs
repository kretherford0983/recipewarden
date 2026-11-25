using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Web.Tests;

/// <summary>Kitchen lookups for the M3 page tests.</summary>
internal static class M3KitchenHelpers
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<int> ToolIdAsync(this RecipeWardenFactory factory, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().KitchenTools
            .Where(t => t.Name == name).OrderByDescending(t => t.Id).Select(t => t.Id).FirstAsync(Ct);
    }

    public static async Task<int> InventoryVersionAsync(this RecipeWardenFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().InventoryVersions.Select(v => v.Version).SingleAsync(Ct);
    }

    public static async Task<int> SeenVersionAsync(this RecipeWardenFactory factory, string slug)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Recipes.Where(r => r.Slug == slug).Select(r => r.InventoryVersionSeen).SingleAsync(Ct);
    }

    public static async Task<string> ReadAsync(this RecipeWardenFactory factory, string url, HttpClient? client = null)
    {
        using var anonymous = factory.CreateHttpsClient();
        var response = await (client ?? anonymous).GetAsync(url, Ct);
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, url);
        return System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct));
    }
}
