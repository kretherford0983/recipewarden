using RecipeWarden.Core.Recipes;
using RecipeWarden.Core.Search;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>FR-SRCH-01, AC-21, AC-01: ranked full-text search over every indexed field of published recipes.</summary>
[Trait("Req", "FR-SRCH-01")]
public sealed class FR_SRCH_01_SearchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Req", "AC-21")]
    public async Task Recipes_with_both_terms_in_any_field_rank_above_those_with_one()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        // One term, but in the title and many times: a strong bm25 score for "chicken" alone.
        await s.AddAsync("Chicken", "1 lb chicken\n1 cup chicken broth", "Cook the chicken. Shred the chicken.");
        await s.AddAsync("Key Lime Pie", "1 cup lime juice", "Bake.");
        // Both terms, spread over different fields.
        await s.AddAsync("Weeknight Tacos", "1 lb chicken thighs", "Squeeze a lime over the top.");
        await s.Recipes.CreateAsync(RecipeTestScope.Edit("Green Soup") with { Notes = "Great with chicken and a lime wedge." }, "u", Ct);

        var results = await s.SearchAsync("chicken lime");

        results.Total.ShouldBe(4);
        results.Items.Take(2).Select(r => r.Title).Order().ShouldBe(["Green Soup", "Weeknight Tacos"]);
        results.Items.Skip(2).Select(r => r.Title).ShouldContain("Chicken");
    }

    [Theory]
    [InlineData("tortilla")]   // title
    [InlineData("smoky")]      // description
    [InlineData("cumin")]      // ingredients
    [InlineData("simmer")]     // steps
    [InlineData("mexican")]    // tags
    [InlineData("freezes")]    // notes
    public async Task Every_indexed_field_is_searchable(string word)
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await s.Recipes.CreateAsync(RecipeTestScope.Edit("Tortilla Soup", "1 tsp cumin", "Simmer gently.", true, "Mexican")
            with { Description = "Smoky.", Notes = "Freezes well." }, "u", Ct);

        (await s.SearchAsync(word)).Items.Single().Title.ShouldBe("Tortilla Soup");
    }

    [Fact]
    [Trait("Req", "AC-01")]
    public async Task A_saved_recipe_is_found_immediately_and_an_edit_updates_the_index()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var created = await s.AddAsync("Saffron Rice");
        (await s.SearchAsync("saffron")).Total.ShouldBe(1);

        await s.Recipes.UpdateAsync(created.Id, RecipeTestScope.Edit("Turmeric Rice"), created.Version, "u", false, Ct);
        (await s.SearchAsync("saffron")).Total.ShouldBe(0);
        (await s.SearchAsync("turmeric")).Total.ShouldBe(1);
    }

    [Fact]
    public async Task Drafts_are_not_searchable_by_readers()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var draft = await s.AddAsync("Secret Sauce", publish: false);
        (await s.SearchAsync("secret")).Total.ShouldBe(0);

        // Publishing adds it; unpublishing removes it again.
        var published = await s.Recipes.UpdateAsync(draft.Id, RecipeTestScope.Edit("Secret Sauce"), draft.Version, "u", false, Ct);
        (await s.SearchAsync("secret")).Total.ShouldBe(1);
        await s.Recipes.UpdateAsync(draft.Id, RecipeTestScope.Edit("Secret Sauce", publish: false), published.Version, "u", false, Ct);
        (await s.SearchAsync("secret")).Total.ShouldBe(0);
    }

    [Fact]
    public async Task Editors_find_drafts_by_title()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await s.AddAsync("Secret Sauce", publish: false);
        await s.AddAsync("Public Soup");

        var drafts = await s.Search.SearchAsync(SearchCriteria.Parse(new SearchInput { Q = "sauce" }), RecipeVisibility.Drafts, Ct);
        drafts.Items.Single().Status.ShouldBe(RecipeStatus.Draft);
        (await s.Search.SearchAsync(SearchCriteria.Parse(new SearchInput()), RecipeVisibility.Drafts, Ct)).Total.ShouldBe(1);
    }

    [Fact]
    public async Task Accents_and_case_do_not_matter_and_the_last_word_matches_as_a_prefix()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await s.AddAsync("Crème Brûlée", "4 egg yolks", "Bake.");

        (await s.SearchAsync("CREME BRULEE")).Total.ShouldBe(1);
        (await s.SearchAsync("crème")).Total.ShouldBe(1);
        (await s.SearchAsync("bru")).Total.ShouldBe(1);
        (await s.SearchAsync("bru ")).Total.ShouldBe(0); // a finished word must match whole
    }

    [Fact]
    public async Task No_match_and_empty_query()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await s.AddAsync("Soup");

        (await s.SearchAsync("zzzz")).Total.ShouldBe(0);
        var all = await s.SearchAsync("   ");
        all.Total.ShouldBe(1);
        all.PageCount.ShouldBe(1);
    }

    [Fact]
    public async Task The_index_can_be_rebuilt()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await s.AddAsync("Saffron Rice");
        await s.AddAsync("Draft Rice", publish: false);

        await s.Recipes.RebuildSearchIndexAsync(Ct);

        (await s.SearchAsync("rice")).Items.Single().Title.ShouldBe("Saffron Rice");
    }

    [Fact]
    public async Task Summaries_are_shortened()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await s.Recipes.CreateAsync(RecipeTestScope.Edit("Long") with { Description = new string('a', 300) }, "u", Ct);
        await s.Recipes.CreateAsync(RecipeTestScope.Edit("Short") with { Description = "Brief." }, "u", Ct);

        var items = (await s.SearchAsync(sort: "title")).Items;
        items[0].Summary!.Length.ShouldBe(161);
        items[0].Summary.ShouldEndWith("…");
        items[1].Summary.ShouldBe("Brief.");
    }
}
