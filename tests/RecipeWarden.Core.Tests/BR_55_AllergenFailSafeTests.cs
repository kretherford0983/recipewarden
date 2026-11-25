using System.Text.RegularExpressions;
using RecipeWarden.Core.Nutrition;

namespace RecipeWarden.Core.Tests;

/// <summary>BR-55, AC-132: a line the allergen check could not finish is never reported as free of allergens.</summary>
[Trait("Req", "FR-ALG-01")]
[Trait("Req", "BR-55")]
public sealed class BR_55_AllergenFailSafeTests
{
    private static bool TimesOut(Regex regex, string text) => throw new RegexMatchTimeoutException(text, regex.ToString(), TimeSpan.FromSeconds(2));

    [Fact]
    [Trait("Req", "AC-132")]
    public void A_line_that_times_out_may_contain_every_allergen()
    {
        AllergenRules.TryDetect("1 cup water", null, TimesOut, out _).ShouldBeFalse();
        AllergenRules.Unchecked.Count.ShouldBe(AllergenRules.All.Count);
        AllergenRules.Unchecked.Values.ShouldAllBe(s => s == AllergenStatus.MayContain);
    }

    [Fact]
    [Trait("Req", "AC-132")]
    public void An_unchecked_line_keeps_the_others_contains_when_merged()
    {
        var merged = AllergenRules.Merge([AllergenRules.Detect("2 eggs"), AllergenRules.Unchecked]);
        merged[AllergenCode.Egg].ShouldBe(AllergenStatus.Contains);
        merged[AllergenCode.Peanuts].ShouldBe(AllergenStatus.MayContain);
        merged.Count.ShouldBe(AllergenRules.All.Count);
    }

    [Fact]
    public void A_checked_line_reports_what_it_found()
    {
        AllergenRules.TryDetect("1 cup milk", null, (r, t) => r.IsMatch(t), out var found).ShouldBeTrue();
        found.ShouldContainKeyAndValue(AllergenCode.Milk, AllergenStatus.Contains);
        AllergenRules.TryDetect("  ", null, TimesOut, out var empty).ShouldBeTrue(); // nothing to check
        empty.ShouldBeEmpty();
    }
}
