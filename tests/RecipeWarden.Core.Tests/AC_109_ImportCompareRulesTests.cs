using RecipeWarden.Core.Import;
using RecipeWarden.Core.Python;

namespace RecipeWarden.Core.Tests;

/// <summary>AC-109, BR-117, D6: how a saved URL import compares between the .NET extractor and a site reader.</summary>
[Trait("Req", "AC-109")]
[Trait("Req", "BR-117")]
public sealed class AC_109_ImportCompareRulesTests
{
    private static ImportedRecipe Ours(string? title, string ingredients, string steps) =>
        new() { Title = title ?? string.Empty, Ingredients = ingredients, Steps = steps, Method = ExtractionMethod.JsonLd };

    private static SiteRecipeReading Site(bool supported, bool found = true, string? title = "Soup", int ingredients = 2, int steps = 1) =>
        new(supported, found, title, [.. Enumerable.Repeat("x", ingredients)], [.. Enumerable.Repeat("y", steps)], null, null, null, null, null, null, null, null);

    [Theory]
    [InlineData("Soup", 1, 1, true)]
    [InlineData("", 1, 1, false)]
    [InlineData("Soup", 0, 1, false)]
    [InlineData("Soup", 1, 0, false)]
    [InlineData(null, 3, 3, false)]
    public void Complete_means_a_title_an_ingredient_and_a_step(string? title, int ingredients, int steps, bool complete) =>
        ImportCompareRules.IsComplete(title, ingredients, steps).ShouldBe(complete);

    [Fact]
    public void Verdicts_cover_every_case()
    {
        var full = Ours("Soup", "1 cup lentils\n\n2 cups broth", "Simmer.");
        var weak = Ours("Soup", "", "");
        ImportCompareRules.Compare("https://a.example/", full, Site(true)).Verdict.ShouldBe(CompareVerdict.BothComplete);
        ImportCompareRules.Compare("https://a.example/", weak, Site(true)).Verdict.ShouldBe(CompareVerdict.OnlySiteReader);
        ImportCompareRules.Compare("https://a.example/", full, Site(true, found: false)).Verdict.ShouldBe(CompareVerdict.OnlyDotNet);
        ImportCompareRules.Compare("https://a.example/", weak, Site(true, title: null)).Verdict.ShouldBe(CompareVerdict.Neither);
        ImportCompareRules.Compare("https://a.example/", null, Site(false, found: false)).Verdict.ShouldBe(CompareVerdict.NoSiteReader);
        ImportCompareRules.Compare("https://a.example/", weak, Site(false)).Verdict.ShouldBe(CompareVerdict.NoSiteReader); // wild mode only
    }

    [Fact]
    public void Counts_are_reported_for_both_readings()
    {
        var row = ImportCompareRules.Compare("https://a.example/", Ours("Soup", "a\n \nb\nc", "s1\ns2"), Site(true, ingredients: 4, steps: 3));
        (row.DotNetIngredients, row.SiteIngredients, row.DotNetSteps, row.SiteSteps).ShouldBe((3, 4, 2, 3));
        row.DotNetMethod.ShouldBe(ExtractionMethod.JsonLd);
        ImportCompareRules.Lines(null).ShouldBe(0);
    }
}
