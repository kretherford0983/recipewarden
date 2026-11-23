using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Web.Tests;

/// <summary>FR-HOME-02, ADR 0044: the home page's recommendation rows. Own host, so other tests' recipes do not mix in.</summary>
[Trait("Req", "FR-HOME-02")]
public sealed class FR_HOME_02_HomeRecommendationTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Favorites_and_not_cooked_lately_rows_show_on_the_home_page_and_empty_rows_do_not()
    {
        using var editor = await factory.SignInAsync("home-recs@example.com");
        var fav = await editor.AddRecipeAsync("Sunday Roast Favorite");
        var due = await editor.AddRecipeAsync("Forgotten Gumbo");
        await editor.AddRecipeAsync("Ordinary Toast");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var favId = await factory.RecipeIdAsync(fav);
            var dueId = await factory.RecipeIdAsync(due);
            await db.Recipes.Where(r => r.Id == favId).ExecuteUpdateAsync(u => u.SetProperty(r => r.Rating, 5), Ct);
            await db.Recipes.Where(r => r.Id == dueId).ExecuteUpdateAsync(u => u
                .SetProperty(r => r.Rating, 4).SetProperty(r => r.LastCookedAt, DateTimeOffset.UtcNow.AddDays(-120)), Ct);
        }

        using var reader = factory.CreateHttpsClient();
        var home = await reader.GetStringAsync("/", Ct);
        var favorites = home.IndexOf("id=\"favorites-heading\"", StringComparison.Ordinal);
        var lately = home.IndexOf("id=\"lately-heading\"", StringComparison.Ordinal);
        var recent = home.IndexOf("id=\"recent-heading\"", StringComparison.Ordinal);
        favorites.ShouldBeGreaterThan(0);
        lately.ShouldBeGreaterThan(favorites);
        recent.ShouldBeGreaterThan(lately);
        home[favorites..lately].ShouldContain("Sunday Roast Favorite");
        home[lately..recent].ShouldContain("Forgotten Gumbo");
        home[favorites..recent].ShouldNotContain("Ordinary Toast");
        home.ShouldNotContain("id=\"season-heading\""); // UX-09: no seasonal recipes, no row
    }
}
