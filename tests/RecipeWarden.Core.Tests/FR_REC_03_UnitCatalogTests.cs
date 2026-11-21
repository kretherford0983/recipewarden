using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-REC-03, section 8.3: the unit catalogue shared by the parser and the Unit table seed (ADR 0009).</summary>
[Trait("Req", "FR-REC-03")]
public sealed class FR_REC_03_UnitCatalogTests
{
    [Fact]
    public void Codes_are_unique_lower_case_and_short()
    {
        var codes = UnitCatalog.All.Select(u => u.Code).ToArray();
        codes.Distinct(StringComparer.Ordinal).Count().ShouldBe(codes.Length);
        codes.ShouldAllBe(c => c.Length <= 10 && c.All(ch => ch >= 'a' && ch <= 'z'));
    }

    [Fact]
    public void No_alias_points_at_two_units()
    {
        var aliases = UnitCatalog.All.SelectMany(u => u.Aliases).ToArray();
        aliases.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(aliases.Length);
        aliases.ShouldNotContain(a => UnitCatalog.CaseSensitiveAliases.ContainsKey(a));
    }

    [Theory]
    [InlineData("cup", 236.5882365)]
    [InlineData("tbsp", 14.78676478125)]
    [InlineData("tsp", 4.92892159375)]
    [InlineData("oz", 28.349523125)]
    [InlineData("lb", 453.59237)]
    [InlineData("l", 1000)]
    public void Base_factors_are_exact(string code, double toBase)
    {
        UnitCatalog.FindByCode(code)!.ToBase.ShouldBe((decimal)toBase);
    }

    [Fact]
    public void Spoons_and_cups_relate_exactly()
    {
        var tsp = UnitCatalog.FindByCode("tsp")!.ToBase;
        var tbsp = UnitCatalog.FindByCode("tbsp")!.ToBase;
        var cup = UnitCatalog.FindByCode("cup")!.ToBase;
        (tbsp / tsp).ShouldBe(3m);
        (cup / tbsp).ShouldBe(16m);
    }

    [Theory]
    [InlineData("Tablespoons", "tbsp")]
    [InlineData("TBSP", "tbsp")]
    [InlineData("T", "tbsp")]
    [InlineData("T.", "tbsp")]
    [InlineData("t", "tsp")]
    [InlineData("c", "cup")]
    [InlineData("C", "cup")]
    [InlineData("L", "l")]
    [InlineData("fluid ounces", "floz")]
    [InlineData("pkg.", "package")]
    public void Aliases_resolve(string alias, string code)
    {
        UnitCatalog.FindByAlias(alias)!.Code.ShouldBe(code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("handfull")]
    [InlineData("chicken")]
    public void Unknown_aliases_and_codes_give_null(string? text)
    {
        UnitCatalog.FindByAlias(text).ShouldBeNull();
        UnitCatalog.FindByCode(text).ShouldBeNull();
    }

    [Fact]
    public void Vague_units_are_flagged()
    {
        UnitCatalog.FindByCode("pinch")!.IsVague.ShouldBeTrue();
        UnitCatalog.FindByCode("cup")!.IsVague.ShouldBeFalse();
    }

    [Fact]
    public void Two_word_aliases_set_the_look_ahead()
    {
        UnitCatalog.MaxAliasWords.ShouldBe(2);
    }

    [Theory]
    [InlineData(0.79, true)]
    [InlineData(0.8, false)]
    [InlineData(1.0, false)]
    public void Ingredient_below_threshold_needs_review(double confidence, bool review)
    {
        new ParsedIngredient { RawText = "x", Confidence = (decimal)confidence }.NeedsReview.ShouldBe(review);
    }

    [Fact]
    public void Ingredient_line_is_a_heading_or_an_ingredient()
    {
        new IngredientLine("Sauce", null).IsHeading.ShouldBeTrue();
        new IngredientLine(null, new ParsedIngredient { RawText = "x" }).IsHeading.ShouldBeFalse();
    }
}
