using RecipeWarden.Core.Home;

namespace RecipeWarden.Core.Tests;

[Trait("Req", "FR-HOME-02")]
public sealed class FR_HOME_02_RecommendationRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private static RecommendationCandidate C(int id, int? rating = null, int? cookedDaysAgo = null, params string[] tags) =>
        new(id, rating, cookedDaysAgo is { } d ? Now.AddDays(-d) : null, Now.AddDays(-id), tags);

    [Theory]
    [InlineData(1, Hemisphere.North, "winter")]
    [InlineData(4, Hemisphere.North, "spring")]
    [InlineData(7, Hemisphere.North, "summer")]
    [InlineData(10, Hemisphere.North, "fall")]
    [InlineData(10, Hemisphere.North, "autumn")]
    [InlineData(11, Hemisphere.North, "holiday")]
    [InlineData(12, Hemisphere.North, "winter")]
    [InlineData(1, Hemisphere.South, "summer")]
    [InlineData(4, Hemisphere.South, "fall")]
    [InlineData(7, Hemisphere.South, "winter")]
    [InlineData(10, Hemisphere.South, "spring")]
    [InlineData(12, Hemisphere.South, "holiday")]
    public void Season_tags_follow_the_month_and_hemisphere(int month, Hemisphere hemisphere, string tag) =>
        RecommendationRules.SeasonTags(month, hemisphere).ShouldContain(tag);

    [Fact]
    public void Holiday_is_only_in_November_and_December_and_months_are_checked()
    {
        RecommendationRules.SeasonTags(10, Hemisphere.North).ShouldNotContain("holiday");
        RecommendationRules.SeasonTags(1, Hemisphere.North).ShouldNotContain("holiday");
        RecommendationRules.SeasonTags(7, Hemisphere.North).Count.ShouldBe(1);
        Should.Throw<ArgumentOutOfRangeException>(() => RecommendationRules.SeasonTags(0, Hemisphere.North));
        Should.Throw<ArgumentOutOfRangeException>(() => RecommendationRules.SeasonTags(13, Hemisphere.North));
    }

    [Fact]
    public void Due_means_rated_4_or_more_and_not_cooked_for_60_days()
    {
        RecommendationRules.IsDue(4, null, Now).ShouldBeTrue();
        RecommendationRules.IsDue(5, Now.AddDays(-60), Now).ShouldBeTrue();
        RecommendationRules.IsDue(5, Now.AddDays(-59), Now).ShouldBeFalse();
        RecommendationRules.IsDue(3, Now.AddDays(-400), Now).ShouldBeFalse();
        RecommendationRules.IsDue(null, null, Now).ShouldBeFalse();
        RecommendationRules.IsFavorite(5).ShouldBeTrue();
        RecommendationRules.IsFavorite(4).ShouldBeFalse();
        RecommendationRules.IsFavorite(null).ShouldBeFalse();
    }

    [Fact]
    public void Each_recipe_appears_in_the_first_row_that_fits()
    {
        var rows = RecommendationRules.Pick(
        [
            C(1, 5, 100, "fall"),     // in season wins over favorite and due
            C(2, 5, 1),               // favorite, cooked recently
            C(3, 4, 90),              // due
            C(4, 4, null),            // due, never cooked: shown before ones cooked long ago
            C(5, 3, 300, "summer"),   // nothing fits
            C(6, null, null, "holiday"),
        ], Now, Hemisphere.North);

        rows[RecommendationKind.InSeason].ShouldBe([1, 6]); // rated first
        rows[RecommendationKind.Favorites].ShouldBe([2]);
        rows[RecommendationKind.NotCookedLately].ShouldBe([4, 3]);
    }

    [Fact]
    [Trait("Req", "AC-60")]
    public void Rows_are_capped_and_stable_for_the_day_but_rotate_between_days()
    {
        var many = Enumerable.Range(1, 40).Select(i => C(i, 5, 1)).ToList();
        var today = RecommendationRules.Pick(many, Now, Hemisphere.North)[RecommendationKind.Favorites];
        today.Count.ShouldBe(RecommendationRules.RowSize);
        RecommendationRules.Pick(many, Now.AddHours(5), Hemisphere.North)[RecommendationKind.Favorites].ShouldBe(today);
        var days = Enumerable.Range(1, 5).Select(d => RecommendationRules.Pick(many, Now.AddDays(d), Hemisphere.North)[RecommendationKind.Favorites]);
        days.ShouldContain(d => !d.SequenceEqual(today));
        RecommendationRules.Pick([], Now, Hemisphere.North).Values.ShouldAllBe(v => v.Count == 0);

        // AC-60: up to 8 recommended in all.
        var mixed = Enumerable.Range(1, 30).Select(i => C(i, i % 2 == 0 ? 5 : 4, 100, i % 3 == 0 ? "fall" : "none")).ToList();
        var rows = RecommendationRules.Pick(mixed, Now, Hemisphere.North);
        rows.Values.Sum(v => v.Count).ShouldBe(RecommendationRules.MaxTotal);
        rows.Values.ShouldAllBe(v => v.Count <= RecommendationRules.RowSize);
        rows.Values.SelectMany(v => v).Distinct().Count().ShouldBe(RecommendationRules.MaxTotal);
        Should.Throw<ArgumentNullException>(() => RecommendationRules.Pick(null!, Now, Hemisphere.North));
    }
}
