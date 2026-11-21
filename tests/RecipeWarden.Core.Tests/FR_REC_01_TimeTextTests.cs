using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-REC-01, ADR 0010: prep, cook and total times as minutes, ranges and hours.</summary>
[Trait("Req", "FR-REC-01")]
public sealed class FR_REC_01_TimeTextTests
{
    [Theory]
    [InlineData("20", 20, null)]
    [InlineData("20 min", 20, null)]
    [InlineData("20 minutes", 20, null)]
    [InlineData("20m", 20, null)]
    [InlineData("20-25", 20, 25)]
    [InlineData("20 - 25 min", 20, 25)]
    [InlineData("20–25 minutes", 20, 25)]
    [InlineData("20 to 25 mins", 20, 25)]
    [InlineData("1 hr", 60, null)]
    [InlineData("1 hour", 60, null)]
    [InlineData("2 hours", 120, null)]
    [InlineData("1h", 60, null)]
    [InlineData("1 hr 15 min", 75, null)]
    [InlineData("1 hour 15 minutes", 75, null)]
    [InlineData("1h15m", 75, null)]
    [InlineData("1h 15m", 75, null)]
    [InlineData("1:15", 75, null)]
    [InlineData("1.5 hours", 90, null)]
    [InlineData("1-1.5 hours", 60, 90)]
    [InlineData("1 to 2 hrs", 60, 120)]
    [InlineData("45 min - 1 hr", 45, 60)]
    [InlineData("1 hr 15 min - 1 hr 30 min", 75, 90)]
    [InlineData("1 hr – 1 hr 30 min", 60, 90)]
    [InlineData("0", 0, null)]
    [InlineData("25-25", 25, null)]
    [InlineData("About 20 minutes", 20, null)]
    [InlineData("1 day", 1440, null)]
    public void Times_are_read_as_minutes(string text, int min, int? max)
    {
        var r = TimeText.Parse(text);

        r.Error.ShouldBeNull(text);
        r.Min.ShouldBe(min);
        r.Max.ShouldBe(max);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Blank_means_no_time(string? text)
    {
        var r = TimeText.Parse(text);
        r.Error.ShouldBeNull();
        r.Min.ShouldBeNull();
        r.Max.ShouldBeNull();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("overnight")]
    [InlineData("20 fortnights")]
    [InlineData("20-")]
    [InlineData("1:75")]
    [InlineData("20-25-30")]
    [InlineData("-5")]
    [InlineData("hr")]
    public void Unreadable_times_get_the_format_hint(string text)
    {
        TimeText.Parse(text).Error.ShouldBe(TimeText.Unreadable);
    }

    [Theory]
    [InlineData("30-20", TimeText.HighBelowLow)]
    [InlineData("8 days", TimeText.TooLong)]
    [InlineData("1-10081", TimeText.TooLong)]
    public void Range_rules(string text, string error)
    {
        TimeText.Parse(text).Error.ShouldBe(error);
    }

    [Theory]
    [InlineData(20, null, "20 min")]
    [InlineData(20, 25, "20–25 min")]
    [InlineData(60, null, "1 hr")]
    [InlineData(75, null, "1 hr 15 min")]
    [InlineData(60, 90, "1 hr – 1 hr 30 min")]
    [InlineData(45, 60, "45 min – 1 hr")]
    [InlineData(0, null, "0 min")]
    public void Times_are_shown_in_hours_and_minutes(int min, int? max, string expected)
    {
        TimeText.Format(min, max).ShouldBe(expected);
    }

    [Fact]
    public void Nothing_formats_as_empty()
    {
        TimeText.Format(null, null).ShouldBe(string.Empty);
    }

    [Theory]
    [InlineData(20, null)]
    [InlineData(20, 25)]
    [InlineData(75, null)]
    [InlineData(60, 90)]
    [InlineData(45, 60)]
    public void Formatted_text_reads_back_the_same(int min, int? max)
    {
        var r = TimeText.Parse(TimeText.Format(min, max));
        (r.Min, r.Max).ShouldBe((min, max));
    }

    [Theory]
    [InlineData(20, 25, null)]
    [InlineData(null, null, null)]
    [InlineData(25, 20, TimeText.HighBelowLow)]
    [InlineData(null, 20, TimeText.HighWithoutLow)]
    [InlineData(-1, null, TimeText.TooLong)]
    [InlineData(10, 20000, TimeText.TooLong)]
    public void Stored_values_are_checked_too(int? min, int? max, string? error)
    {
        TimeText.ValidateRange(min, max).ShouldBe(error);
    }

    [Theory]
    [InlineData(20, null, 10, null, 30, null)]
    [InlineData(20, 25, 10, 15, 30, 40)]
    [InlineData(20, null, 10, 15, 30, 35)]
    [InlineData(null, null, 10, null, 10, null)]
    [InlineData(null, null, null, null, null, null)]
    public void Total_from_prep_and_cook_adds_end_by_end(int? prep, int? prepMax, int? cook, int? cookMax, int? total, int? totalMax)
    {
        TimeText.Sum(prep, prepMax, cook, cookMax).ShouldBe((total, totalMax));
    }
}
