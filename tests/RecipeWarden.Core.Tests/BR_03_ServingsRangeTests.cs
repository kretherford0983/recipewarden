using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>BR-03, ADR 0010: servings are a number or a range ("6-8"), shown as "6–8".</summary>
[Trait("Req", "BR-03")]
[Trait("Req", "FR-REC-01")]
public sealed class BR_03_ServingsRangeTests
{
    [Theory]
    [InlineData("6", 6.0, null)]
    [InlineData(" 6 ", 6.0, null)]
    [InlineData("2.5", 2.5, null)]
    [InlineData("6-8", 6.0, 8.0)]
    [InlineData("6 - 8", 6.0, 8.0)]
    [InlineData("6–8", 6.0, 8.0)]
    [InlineData("6—8", 6.0, 8.0)]
    [InlineData("6 to 8", 6.0, 8.0)]
    [InlineData("6 TO 8", 6.0, 8.0)]
    [InlineData("Serves 6-8", 6.0, 8.0)]
    [InlineData("6-8 servings", 6.0, 8.0)]
    [InlineData("makes 12", 12.0, null)]
    [InlineData("4 people", 4.0, null)]
    [InlineData("6-6", 6.0, null)]
    public void Numbers_and_ranges_are_read(string text, double min, double? max)
    {
        var r = ServingsRules.ParseText(text);

        r.Error.ShouldBeNull();
        r.Min.ShouldBe((decimal)min);
        r.Max.ShouldBe(max is null ? null : (decimal)max);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_means_unknown(string? text)
    {
        var r = ServingsRules.ParseText(text);
        r.Error.ShouldBeNull();
        r.Min.ShouldBeNull();
        r.Max.ShouldBeNull();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("six")]
    [InlineData("6-")]
    [InlineData("6-8-10")]
    [InlineData("6/8")]
    [InlineData("-6")]
    public void Unreadable_text_gets_the_format_hint(string text)
    {
        ServingsRules.ParseText(text).Error.ShouldBe(ServingsRules.Unreadable);
    }

    [Theory]
    [InlineData("8-6", ServingsRules.HighBelowLow)]
    [InlineData("0", ServingsRules.NotPositive)]
    [InlineData("0-4", ServingsRules.NotPositive)]
    [InlineData("4-1001", ServingsRules.TooMany)]
    [InlineData("2000", ServingsRules.TooMany)]
    public void Ranges_follow_BR_03_at_both_ends(string text, string error)
    {
        ServingsRules.ParseText(text).Error.ShouldBe(error);
    }

    [Theory]
    [InlineData(6.0, null, "6")]
    [InlineData(6.0, 8.0, "6–8")]
    [InlineData(2.5, null, "2½")]
    [InlineData(1.5, 2.5, "1½–2½")]
    public void Ranges_are_shown_with_an_en_dash(double min, double? max, string expected)
    {
        ServingsRules.Format((decimal)min, max is null ? null : (decimal)max).ShouldBe(expected);
    }

    [Fact]
    public void Formatted_text_reads_back_the_same()
    {
        var r = ServingsRules.ParseText(ServingsRules.Format(6m, 8m));
        (r.Min, r.Max).ShouldBe((6m, 8m));
    }

    [Theory]
    [InlineData(6.0, 8.0, null)]
    [InlineData(6.0, null, null)]
    [InlineData(null, null, null)]
    [InlineData(8.0, 6.0, ServingsRules.HighBelowLow)]
    [InlineData(null, 8.0, ServingsRules.HighWithoutLow)]
    [InlineData(6.0, 1001.0, ServingsRules.TooMany)]
    public void Stored_values_are_checked_too(double? min, double? max, string? error)
    {
        ServingsRules.ValidateRange(min is null ? null : (decimal)min, max is null ? null : (decimal)max).ShouldBe(error);
    }
}
