using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Tests.Shared;

namespace RecipeWarden.Infrastructure.Tests;

[Trait("Req", "FR-HOME-02")]
public sealed class FR_HOME_02_RecommendationServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Rows_come_from_published_recipes_by_season_tag_rating_and_last_cooked()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero));
        await using var s = await RecipeTestScope.CreateAsync(clock);
        var summer = await s.AddAsync("Peach Salad", tags: "Summer");
        var winter = await s.AddAsync("Beef Stew", tags: "Winter");
        var favorite = await s.AddAsync("House Pizza");
        var due = await s.AddAsync("Old Lasagna");
        var cooked = await s.AddAsync("Weekly Tacos");
        var draft = await s.AddAsync("Draft Pie", publish: false);

        async Task Set(int id, int? rating, DateTimeOffset? cookedAt)
        {
            var r = await s.Db.Recipes.SingleAsync(x => x.Id == id, Ct);
            r.Rating = rating;
            r.LastCookedAt = cookedAt;
        }

        await Set(favorite.Id, 5, clock.GetUtcNow().AddDays(-3));
        await Set(due.Id, 4, clock.GetUtcNow().AddDays(-90));
        await Set(cooked.Id, 4, clock.GetUtcNow().AddDays(-10));
        await Set(draft.Id, 5, null);
        await Set(winter.Id, 5, clock.GetUtcNow().AddDays(-1));
        await s.Db.SaveChangesAsync(Ct);

        var home = s.NewScope().ServiceProvider;
        var recs = await home.GetRequiredService<RecommendationService>().GetAsync(Ct);
        recs.InSeason.Select(c => c.Title).ShouldBe(["Peach Salad"]);
        recs.Favorites.Select(c => c.Title).ShouldBe(["Beef Stew", "House Pizza"], ignoreOrder: true);
        recs.NotCookedLately.Select(c => c.Title).ShouldBe(["Old Lasagna"]);
        recs.IsEmpty.ShouldBeFalse();

        // Southern hemisphere: July is winter.
        var south = new RecommendationService(home.GetRequiredService<Data.AppDbContext>(), clock,
            new HomeOptions { Hemisphere = Core.Home.Hemisphere.South });
        (await south.GetAsync(Ct)).InSeason.Select(c => c.Title).ShouldBe(["Beef Stew"]);
    }

    [Fact]
    public async Task An_empty_library_has_no_recommendations()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await s.AddAsync("Plain Toast");
        (await s.NewScope().ServiceProvider.GetRequiredService<RecommendationService>().GetAsync(Ct)).IsEmpty.ShouldBeTrue();
    }
}
