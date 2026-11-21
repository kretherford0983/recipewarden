using Microsoft.EntityFrameworkCore;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>FR-SRCH-02: tag, collection, time and rating filters; newest, title and last-cooked sorts; paging.</summary>
[Trait("Req", "FR-SRCH-02")]
public sealed class FR_SRCH_02_SearchFilterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tags_filter_with_and()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await s.AddAsync("Soup", tags: ["dinner", "soup"]);
        await s.AddAsync("Stew", tags: ["dinner"]);
        await s.AddAsync("Toast", tags: ["breakfast"]);

        (await s.SearchAsync(tags: ["dinner"])).Total.ShouldBe(2);
        (await s.SearchAsync(tags: ["dinner", "soup"])).Items.Single().Title.ShouldBe("Soup");
        (await s.SearchAsync(tags: ["nothing"])).Total.ShouldBe(0);
    }

    [Fact]
    public async Task Collection_filter()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var soup = await s.AddAsync("Soup");
        await s.AddAsync("Stew");
        var c = await s.Collections.CreateAsync("Winter", null, "u", Ct);
        await s.Collections.AddRecipeAsync(c.Id, soup.Id, Ct);

        (await s.SearchAsync(collection: "winter")).Items.Single().Title.ShouldBe("Soup");
    }

    [Fact]
    public async Task Max_time_uses_total_or_prep_plus_cook_and_skips_recipes_without_times()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await s.Recipes.CreateAsync(RecipeTestScope.Edit("Quick") with { TotalMinutes = 20 }, "u", Ct);
        await s.Recipes.CreateAsync(RecipeTestScope.Edit("Parts") with { PrepMinutes = 10, CookMinutes = 15 }, "u", Ct);
        await s.Recipes.CreateAsync(RecipeTestScope.Edit("Slow") with { TotalMinutes = 240 }, "u", Ct);
        await s.AddAsync("Unknown");

        (await s.SearchAsync(maxMinutes: "30", sort: "title")).Items.Select(r => r.Title).ShouldBe(["Parts", "Quick"]);
        (await s.SearchAsync(sort: "title")).Items.Single(r => r.Title == "Parts").TotalMinutes.ShouldBe(25);
    }

    [Fact]
    public async Task Rating_filter_and_last_cooked_sort_use_their_columns()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var a = await s.AddAsync("A");
        var b = await s.AddAsync("B");
        await s.AddAsync("C");
        // FR-REC-07 (rating, last cooked) has no editor yet; set the columns directly.
        await s.Db.Recipes.Where(r => r.Id == a.Id).ExecuteUpdateAsync(u => u
            .SetProperty(r => r.Rating, 5).SetProperty(r => r.LastCookedAt, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)), Ct);
        await s.Db.Recipes.Where(r => r.Id == b.Id).ExecuteUpdateAsync(u => u
            .SetProperty(r => r.Rating, 3).SetProperty(r => r.LastCookedAt, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)), Ct);

        (await s.SearchAsync(minRating: "4")).Items.Single().Title.ShouldBe("A");
        (await s.SearchAsync(sort: "lastcooked")).Items.Select(r => r.Title).ShouldBe(["B", "A", "C"]);
    }

    [Fact]
    public async Task Newest_first_by_default_and_title_sort_ignores_case()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await s.AddAsync("banana bread");
        await s.AddAsync("Apple pie");
        await s.AddAsync("Cherry tart");

        (await s.SearchAsync()).Items.Select(r => r.Title).ShouldBe(["Cherry tart", "Apple pie", "banana bread"]);
        (await s.SearchAsync(sort: "title")).Items.Select(r => r.Title).ShouldBe(["Apple pie", "banana bread", "Cherry tart"]);
    }

    [Fact]
    public async Task Results_are_paged_by_24()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        for (var i = 0; i < 26; i++)
        {
            await s.AddAsync($"Lime recipe {i:00}");
        }

        var first = await s.SearchAsync("lime", page: "1");
        var second = await s.SearchAsync("lime", page: "2");
        first.Total.ShouldBe(26);
        first.PageCount.ShouldBe(2);
        first.Items.Count.ShouldBe(24);
        second.Items.Count.ShouldBe(2);
        first.Items.Concat(second.Items).Select(r => r.Id).Distinct().Count().ShouldBe(26);

        (await s.SearchAsync(sort: "newest", page: "2")).Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Search_and_filters_combine()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await s.AddAsync("Lime Soup", tags: ["dinner"]);
        await s.AddAsync("Lime Pie", tags: ["dessert"]);

        (await s.SearchAsync("lime", tags: ["dessert"])).Items.Single().Title.ShouldBe("Lime Pie");
    }
}
