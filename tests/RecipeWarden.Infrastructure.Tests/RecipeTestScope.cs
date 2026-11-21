using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Core.Search;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>One test database plus the recipe services from one DI scope, as a page request would see them.</summary>
internal sealed class RecipeTestScope : IAsyncDisposable
{
    public const string UserId = "user-1";

    private readonly TestDatabase tdb;
    private readonly AsyncServiceScope scope;

    private RecipeTestScope(TestDatabase tdb)
    {
        this.tdb = tdb;
        scope = tdb.Provider.CreateAsyncScope();
    }

    public AppDbContext Db => scope.ServiceProvider.GetRequiredService<AppDbContext>();
    public RecipeService Recipes => scope.ServiceProvider.GetRequiredService<RecipeService>();
    public RecipeSearchService Search => scope.ServiceProvider.GetRequiredService<RecipeSearchService>();
    public TagService Tags => scope.ServiceProvider.GetRequiredService<TagService>();
    public CollectionService Collections => scope.ServiceProvider.GetRequiredService<CollectionService>();

    public static async Task<RecipeTestScope> CreateAsync(TimeProvider? clock = null) =>
        new(await TestDatabase.CreateAsync(clock: clock));

    /// <summary>A second, independent scope on the same database (another user's request).</summary>
    public AsyncServiceScope NewScope() => tdb.Provider.CreateAsyncScope();

    public static RecipeEdit Edit(string title, string ingredients = "1 cup flour", string steps = "Mix.", bool publish = true,
        params string[] tags) => new()
    {
        Title = title,
        IngredientsText = ingredients,
        StepsText = steps,
        WantsPublish = publish,
        Tags = tags,
    };

    public async Task<SaveOutcome> AddAsync(string title, string ingredients = "1 cup flour", string steps = "Mix.",
        bool publish = true, params string[] tags)
    {
        var outcome = await Recipes.CreateAsync(Edit(title, ingredients, steps, publish, tags), UserId, TestContext.Current.CancellationToken);
        outcome.Status.ShouldBe(SaveStatus.Saved);
        return outcome;
    }

    public Task<SearchPage> SearchAsync(string? q = null, string? sort = null, string[]? tags = null, string? collection = null,
        string? maxMinutes = null, string? minRating = null, string? page = null) =>
        Search.SearchAsync(SearchCriteria.Parse(new SearchInput
        {
            Q = q,
            Sort = sort,
            Tags = tags,
            Collection = collection,
            MaxMinutes = maxMinutes,
            MinRating = minRating,
            Page = page,
        }), RecipeVisibility.Published, TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync();
        await tdb.DisposeAsync();
    }
}
