using System.Diagnostics;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Organization;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Web.Tests;

/// <summary>Performance tests run alone, after the parallel tests, so other classes do not skew the timings.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PerformanceGroup
{
    public const string Name = "Performance";
}

/// <summary>
/// AC-95, NFR-01, ADR 0045: with 5,000 published recipes, p95 server time is under 300 ms for the home, list and
/// recipe pages and under 200 ms for search. Timed in process (no network), after a warm-up, alone in the run.
/// </summary>
[Trait("Req", "AC-95")]
[Trait("Req", "NFR-01")]
[Collection(PerformanceGroup.Name)]
public sealed class AC_95_PerformanceTests(AC_95_PerformanceTests.Library library) : IClassFixture<AC_95_PerformanceTests.Library>
{
    public const int RecipeCount = 5000;
    public const double PageBudgetMs = 300;
    public const double SearchBudgetMs = 200;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] Words =
    [
        "chicken", "beef", "pork", "tofu", "salmon", "lentil", "mushroom", "pumpkin", "lemon", "garlic", "ginger", "basil", "chili",
        "coconut", "tomato", "spinach", "potato", "apple", "cherry", "almond", "honey", "maple", "smoky", "crispy", "creamy", "spicy",
        "roasted", "braised", "grilled", "quick",
    ];

    private static readonly string[] Dishes = ["Stew", "Curry", "Salad", "Soup", "Pie", "Tacos", "Pasta", "Bake", "Stir-Fry", "Bowl", "Tart", "Bread"];

    /// <summary>One host with 5,000 recipes cloned (with fresh titles and tags) from one recipe saved through the real pipeline.</summary>
    public sealed class Library : IAsyncLifetime, IDisposable
    {
        public RecipeWardenFactory Host { get; } = new();

        public List<string> Slugs { get; } = [];

        public async ValueTask InitializeAsync()
        {
            Host.Settings["RateLimits:SearchPerMinute"] = "100000";
            using var client = Host.CreateHttpsClient(); // starts the host
            await using var scope = Host.Services.CreateAsyncScope();
            var ct = TestContext.Current.CancellationToken;
            var recipes = scope.ServiceProvider.GetRequiredService<RecipeService>();
            var template = await recipes.CreateAsync(new RecipeEdit
            {
                Title = "Template Chicken Stew",
                Description = "A weeknight stew with **plenty** of vegetables.",
                IngredientsText = "For the stew:\n2 lb chicken thighs, cut up\n1 tbsp olive oil\n1 onion, chopped\n3 cloves garlic, minced\n2 carrots, sliced\n" +
                    "2 potatoes, cubed\n1 (14.5 oz) can diced tomatoes\n2 cups chicken broth\n1 tsp salt\n½ tsp black pepper\nTo finish:\n2 tbsp parsley",
                StepsText = "Brown the chicken in the oil.\nSoften the onion and garlic.\nAdd the carrots, potatoes, tomatoes and broth.\n" +
                    "Simmer 40 minutes.\nSeason and scatter the parsley.",
                PrepMinutes = 20,
                CookMinutes = 60,
                Notes = "Freezes well.",
                Tags = ["Dinner"],
                WantsPublish = true,
            }, "seed", ct);

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var source = await db.Recipes.AsNoTracking().Include(r => r.Ingredients).Include(r => r.Steps).Include(r => r.IngredientGroups)
                .SingleAsync(r => r.Id == template.Id, ct);
            var tagNames = Words.Take(20).Select(w => char.ToUpperInvariant(w[0]) + w[1..]).Concat(["Spring", "Summer", "Fall", "Winter"]).ToList();
            var existing = await db.Tags.ToDictionaryAsync(t => t.NormalizedName, ct);
            var now = DateTimeOffset.UtcNow;
            foreach (var name in tagNames.Where(n => !existing.ContainsKey(NameRules.Normalize(n))))
            {
                var tag = new Tag { Name = name, NormalizedName = NameRules.Normalize(name), Slug = NameRules.Normalize(name), CreatedAt = now, UpdatedAt = now };
                db.Tags.Add(tag);
                existing[tag.NormalizedName] = tag;
            }

            await db.SaveChangesAsync(ct);
            var tagIds = tagNames.Select(n => existing[NameRules.Normalize(n)].Id).ToList();

            var random = new Random(95);
            for (var batch = 0; batch < RecipeCount / 500; batch++)
            {
                db.ChangeTracker.Clear();
                for (var i = 0; i < 500; i++)
                {
                    var n = batch * 500 + i;
                    var title = $"{Cap(Words[random.Next(Words.Length)])} {Cap(Words[random.Next(Words.Length)])} {Dishes[random.Next(Dishes.Length)]} {n}";
                    var slug = "perf-" + n;
                    Slugs.Add(slug);
                    var recipe = new Recipe
                    {
                        Slug = slug, Title = title, Description = source.Description + " " + Words[random.Next(Words.Length)],
                        Servings = source.Servings, PrepMinutes = random.Next(5, 40), CookMinutes = random.Next(10, 180), Status = RecipeStatus.Published,
                        SourceType = SourceType.Manual, Notes = source.Notes, Rating = random.Next(0, 6) is var r and > 0 ? r : null,
                        LastCookedAt = random.Next(3) == 0 ? null : now.AddDays(-random.Next(1, 400)),
                        CreatedAt = now.AddMinutes(-n), UpdatedAt = now.AddMinutes(-n), CreatedById = source.CreatedById, UpdatedById = source.UpdatedById,
                        Version = 1,
                    };
                    var groups = source.IngredientGroups.ToDictionary(g => g.Id, g => new IngredientGroup { Name = g.Name, SortOrder = g.SortOrder });
                    recipe.IngredientGroups.AddRange(groups.Values);
                    foreach (var line in source.Ingredients)
                    {
                        var copy = new RecipeIngredient
                        {
                            SortOrder = line.SortOrder, RawText = line.RawText, Quantity = line.Quantity, QuantityMax = line.QuantityMax, QuantityText = line.QuantityText,
                            UnitId = line.UnitId, Name = line.Name, Preparation = line.Preparation, State = line.State, IsOptional = line.IsOptional, Note = line.Note,
                            ParseConfidence = line.ParseConfidence, FoodId = line.FoodId,
                        };
                        if (line.GroupId is { } g)
                        {
                            copy.Group = groups[g];
                        }

                        recipe.Ingredients.Add(copy);
                    }

                    recipe.Steps.AddRange(source.Steps.Select(s => new RecipeStep { SortOrder = s.SortOrder, Text = s.Text, SectionName = s.SectionName, Title = s.Title }));
                    recipe.Tags.AddRange(tagIds.OrderBy(_ => random.Next()).Take(random.Next(1, 4)).Select(id => new RecipeTag { TagId = id }));
                    db.Recipes.Add(recipe);
                }

                await db.SaveChangesAsync(ct);
            }

            await recipes.RebuildSearchIndexAsync(ct);
        }

        private static string Cap(string s) => char.ToUpperInvariant(s[0]) + s[1..];

        public void Dispose() => Host.Dispose();

        ValueTask IAsyncDisposable.DisposeAsync()
        {
            Host.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private static async Task<(double P95, string Slowest)> P95Async(HttpClient client, IReadOnlyList<string> paths)
    {
        foreach (var path in paths.Take(5))
        {
            (await client.GetAsync(path, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK, path); // warm up
        }

        var times = new List<double>();
        var slow = new List<(double, string)>();
        foreach (var path in paths)
        {
            var watch = Stopwatch.StartNew();
            using var response = await client.GetAsync(path, Ct);
            _ = await response.Content.ReadAsByteArrayAsync(Ct);
            watch.Stop();
            response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
            times.Add(watch.Elapsed.TotalMilliseconds);
            slow.Add((watch.Elapsed.TotalMilliseconds, path));
        }

        times.Sort();

        return (times[(int)Math.Ceiling(0.95 * times.Count) - 1],
            "slowest: " + string.Join("; ", slow.OrderByDescending(x => x.Item1).Take(6).Select(x => $"{x.Item2} {x.Item1:F0} ms")));
    }

    [Fact]
    public async Task The_library_holds_5000_published_recipes()
    {
        await using var scope = library.Host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Recipes.CountAsync(r => r.Status == RecipeStatus.Published, Ct))
            .ShouldBeGreaterThanOrEqualTo(RecipeCount);
    }

    [Fact]
    public async Task Home_list_and_recipe_pages_render_within_300_ms_at_p95()
    {
        using var client = library.Host.CreateHttpsClient();
        var random = new Random(1);
        var paths = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            paths.Add("/");
            paths.Add($"/recipes?page={random.Next(1, 200)}");
            paths.Add("/recipes/" + library.Slugs[random.Next(library.Slugs.Count)]);
        }

        var (p95, slowest) = await P95Async(client, paths);
        p95.ShouldBeLessThan(PageBudgetMs, slowest);
    }

    [Fact]
    public async Task Search_returns_within_200_ms_at_p95()
    {
        using var client = library.Host.CreateHttpsClient();
        var random = new Random(2);
        var paths = Enumerable.Range(0, 60).Select(i => (i % 3) switch
        {
            0 => "/search?q=" + Words[random.Next(Words.Length)],
            1 => "/search?q=" + Words[random.Next(Words.Length)] + "+" + Dishes[random.Next(Dishes.Length)].ToLowerInvariant(),
            _ => "/search?q=" + Words[random.Next(Words.Length)] + "&sort=newest&page=2",
        }).ToList();

        var (p95, slowest) = await P95Async(client, paths);
        p95.ShouldBeLessThan(SearchBudgetMs, slowest);
    }
}
