using RecipeWarden.Core.Search;

namespace RecipeWarden.Core.Tests;

/// <summary>
/// FR-SRCH-02: filters and sort come from the query string and are checked against allow-lists, never passed through.
/// Allergen ("free of") is M6 and has-image is M4 (ADR 0009).
/// </summary>
[Trait("Req", "FR-SRCH-02")]
public sealed class FR_SRCH_02_SearchFilterRulesTests
{
    [Fact]
    public void Defaults_with_no_query_sort_newest_first()
    {
        var c = SearchCriteria.Parse(new SearchInput());

        c.Query.IsEmpty.ShouldBeTrue();
        c.Sort.ShouldBe(SearchSort.Newest);
        c.TagSlugs.ShouldBeEmpty();
        c.CollectionSlug.ShouldBeNull();
        c.MaxTotalMinutes.ShouldBeNull();
        c.MinRating.ShouldBeNull();
        c.Page.ShouldBe(1);
    }

    [Fact]
    public void A_query_defaults_to_relevance()
    {
        SearchCriteria.Parse(new SearchInput { Q = "soup" }).Sort.ShouldBe(SearchSort.Relevance);
    }

    [Theory]
    [InlineData("relevance", SearchSort.Relevance)]
    [InlineData("newest", SearchSort.Newest)]
    [InlineData("title", SearchSort.Title)]
    [InlineData("lastcooked", SearchSort.LastCooked)]
    [InlineData("TITLE", SearchSort.Title)]
    public void Known_sorts_are_accepted(string sort, SearchSort expected)
    {
        SearchCriteria.Parse(new SearchInput { Q = "soup", Sort = sort }).Sort.ShouldBe(expected);
    }

    [Theory]
    [InlineData("Title DESC; DROP TABLE Recipe")]
    [InlineData("2")]
    [InlineData("")]
    public void Unknown_sorts_fall_back_to_the_default(string sort)
    {
        SearchCriteria.Parse(new SearchInput { Sort = sort }).Sort.ShouldBe(SearchSort.Newest);
    }

    [Fact]
    public void Relevance_without_a_query_falls_back_to_newest()
    {
        SearchCriteria.Parse(new SearchInput { Sort = "relevance" }).Sort.ShouldBe(SearchSort.Newest);
    }

    [Fact]
    public void Tag_slugs_are_kept_only_when_valid_and_de_duplicated()
    {
        var c = SearchCriteria.Parse(new SearchInput { Tags = ["dinner", "../x", "Dinner", "dinner", "soup", null!, ""] });
        c.TagSlugs.ShouldBe(["dinner", "soup"]);
    }

    [Fact]
    public void At_most_ten_tags_are_used()
    {
        var c = SearchCriteria.Parse(new SearchInput { Tags = Enumerable.Range(1, 20).Select(i => $"t{i}").ToArray() });
        c.TagSlugs.Count.ShouldBe(SearchCriteria.MaxTags);
    }

    [Theory]
    [InlineData("weeknight", "weeknight")]
    [InlineData("Week Night", null)]
    [InlineData("", null)]
    public void Collection_slug_must_be_valid(string slug, string? expected)
    {
        SearchCriteria.Parse(new SearchInput { Collection = slug }).CollectionSlug.ShouldBe(expected);
    }

    [Theory]
    [InlineData("30", 30)]
    [InlineData("1", 1)]
    [InlineData("1440", 1440)]
    [InlineData("0", null)]
    [InlineData("1441", null)]
    [InlineData("-5", null)]
    [InlineData("abc", null)]
    [InlineData("30.5", null)]
    [InlineData(null, null)]
    public void Max_time_is_a_whole_number_of_minutes_up_to_a_day(string? text, int? expected)
    {
        SearchCriteria.Parse(new SearchInput { MaxMinutes = text }).MaxTotalMinutes.ShouldBe(expected);
    }

    [Theory]
    [InlineData("4", 4)]
    [InlineData("1", 1)]
    [InlineData("5", 5)]
    [InlineData("6", null)]
    [InlineData("0", null)]
    [InlineData("x", null)]
    public void Minimum_rating_is_one_to_five(string text, int? expected)
    {
        SearchCriteria.Parse(new SearchInput { MinRating = text }).MinRating.ShouldBe(expected);
    }

    [Theory]
    [InlineData("3", 3)]
    [InlineData("0", 1)]
    [InlineData("-1", 1)]
    [InlineData("x", 1)]
    [InlineData("100000", 1000)]
    public void Page_is_clamped(string text, int expected)
    {
        SearchCriteria.Parse(new SearchInput { Page = text }).Page.ShouldBe(expected);
    }

    [Fact]
    public void Page_size_is_fixed()
    {
        SearchCriteria.PageSize.ShouldBe(24);
        SearchCriteria.Parse(new SearchInput { Page = "3" }).Skip.ShouldBe(48);
    }

    [Fact]
    public void Query_goes_through_the_fts_builder()
    {
        SearchCriteria.Parse(new SearchInput { Q = "\" OR 1=1 --" }).Query.Terms.ShouldBe(["or", "1"]);
    }
}
