using RecipeWarden.Core.Import;
using RecipeWarden.Core.Python;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>BR-118, FR-IMP-11, D4: the .NET parse is the record; a differing NLP reading is only flagged.</summary>
[Trait("Req", "FR-IMP-11")]
[Trait("Req", "BR-118")]
public sealed class FR_IMP_11_SecondOpinionRulesTests
{
    private static ParsedIngredient Ours(decimal? qty, string? unit, string name, decimal? max = null, string? prep = null, string? note = null) => new()
    {
        RawText = "x", Quantity = qty, QuantityMax = max, UnitCode = unit, Name = name, Preparation = prep, Note = note, Confidence = 1m,
    };

    private static IngredientReading Theirs(decimal? qty, string? unit, string name, decimal? max = null, string? prep = null) =>
        new(qty, max, unit, [name], prep, null, []);

    [Fact]
    [Trait("Req", "AC-104")]
    public void One_and_a_half_cups_read_as_a_half_to_one_is_flagged_with_the_other_reading()
    {
        var opinion = SecondOpinionRules.Compare(Ours(1.5m, "cup", "milk"), Theirs(0.5m, "cup", "milk", max: 1m))!;
        opinion.Differences.ShouldBe(ReadingDifference.Quantity);
        opinion.OtherReading.ShouldBe("½–1 cup milk");
        opinion.Summary.ShouldBe("A second reading differs on the amount: ½–1 cup milk");
    }

    [Theory]
    [InlineData(2.0, null, "large eggs", 2.0, null, "eggs")]                  // size words and plurals do not count
    [InlineData(1.0, "cup", "all-purpose flour, sifted", 1.0, "cups", "all-purpose flour")] // unit aliases
    [InlineData(1.0, "tbsp", "olive oil", 1.0, "tablespoon", "olive oil")]
    [InlineData(0.3333, "cup", "sugar", 0.3336, "cup", "sugar")]        // within 0.001
    [InlineData(1.0, "cup", "Diced Tomatoes", 1.0, "cup", "tomatoes")]
    [InlineData(null, null, "salt", null, null, "salt")]
    public void Readings_that_agree_after_normalizing_are_not_flagged(double? q, string? unit, string name, double? tq, string? tunit, string tname) =>
        SecondOpinionRules.Compare(Ours((decimal?)q, unit, name), Theirs((decimal?)tq, tunit, tname)).ShouldBeNull();

    [Fact]
    public void Preparation_and_notes_are_never_compared() =>
        SecondOpinionRules.Compare(Ours(1m, "cup", "onion", prep: "diced", note: "about 1 medium"), Theirs(1m, "cup", "onion", prep: "chopped")).ShouldBeNull();

    [Theory]
    [InlineData(2.0, "cup", "flour", 2.0, "tbsp", "flour", ReadingDifference.Unit)]
    [InlineData(2.0, "cup", "flour", 2.0, "cup", "sugar", ReadingDifference.Food)]
    [InlineData(2.0, null, "eggs", 2.0, "furlong", "eggs", ReadingDifference.Unit)] // an unknown unit is a difference
    [InlineData(2.0, "cup", "flour", 3.0, "tbsp", "rice", ReadingDifference.Quantity | ReadingDifference.Unit | ReadingDifference.Food)]
    [InlineData(null, null, "salt", 1.0, null, "salt", ReadingDifference.Quantity)]
    public void Quantity_unit_and_food_differences_are_flagged(double? q, string? unit, string name, double? tq, string? tunit, string tname, ReadingDifference expected) =>
        SecondOpinionRules.Compare(Ours((decimal?)q, unit, name), Theirs((decimal?)tq, tunit, tname))!.Differences.ShouldBe(expected);

    [Fact]
    public void A_range_compares_both_ends()
    {
        SecondOpinionRules.Compare(Ours(2m, "cup", "stock", max: 3m), Theirs(2m, "cups", "stock", max: 3m)).ShouldBeNull();
        SecondOpinionRules.Compare(Ours(2m, "cup", "stock", max: 3m), Theirs(2m, "cups", "stock", max: 4m))!.Differences.ShouldBe(ReadingDifference.Quantity);
        SecondOpinionRules.Compare(Ours(2m, "cup", "stock"), Theirs(2m, "cups", "stock", max: 3m))!.Differences.ShouldBe(ReadingDifference.Quantity);
    }

    [Fact]
    public void Several_names_compare_as_one_list()
    {
        var both = new IngredientReading(null, null, null, ["salt", "pepper"], null, "to taste", []);
        SecondOpinionRules.Compare(Ours(null, null, "salt and pepper"), both).ShouldBeNull();
    }

    [Fact]
    public void No_reading_or_no_names_flags_nothing_about_the_food()
    {
        SecondOpinionRules.Compare(Ours(1m, "cup", "milk"), null).ShouldBeNull();
        SecondOpinionRules.Compare(Ours(1m, "cup", "milk"), new IngredientReading(1m, null, "cup", [], null, null, [])).ShouldBeNull();
        SecondOpinionRules.Compare(Ours(1m, "cup", ""), Theirs(1m, "cup", "milk")).ShouldBeNull();
    }

    [Theory]
    [InlineData(0.333333, null, "⅓")]
    [InlineData(2.0, 3.0, "2–3")]
    [InlineData(1.2345, null, "1.235")]
    public void The_other_reading_is_plain_text(double q, double? max, string start) =>
        SecondOpinionRules.Describe(new IngredientReading((decimal)q, (decimal?)max, null, ["x"], null, null, [])).ShouldStartWith(start);

    [Fact]
    public void An_empty_reading_and_a_huge_one_are_described_safely()
    {
        SecondOpinionRules.Describe(new IngredientReading(null, null, null, [], null, null, [])).ShouldBe("(nothing recognised)");
        SecondOpinionRules.Describe(new IngredientReading(1m, null, "cup", [new string('a', 5000)], null, null, [])).Length.ShouldBe(300);
    }

    [Fact]
    public void Summary_names_every_difference()
    {
        new SecondOpinion(ReadingDifference.Unit | ReadingDifference.Food, "2 tbsp rice").Summary
            .ShouldBe("A second reading differs on the unit and ingredient: 2 tbsp rice");
    }

    [Fact]
    public void A_reading_made_from_our_own_parse_never_disagrees()
    {
        // Property: for every unit in the catalog and a spread of amounts, echoing our parse back agrees with it.
        foreach (var unit in Measurement.UnitCatalog.All)
        {
            foreach (var q in new[] { 0.125m, 0.5m, 1m, 1.5m, 2.25m, 12m })
            {
                var ours = Ours(q, unit.Code, "fresh basil leaves", max: q + 1m);
                SecondOpinionRules.Compare(ours, Theirs(q, unit.Aliases[0], "basil leaves", max: q + 1m)).ShouldBeNull($"{q} {unit.Code}");
            }
        }
    }
}
