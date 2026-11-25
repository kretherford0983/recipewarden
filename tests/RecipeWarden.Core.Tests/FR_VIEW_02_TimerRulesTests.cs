using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-VIEW-02, AC-23, ADR 0031: cook mode finds timers in step text.</summary>
[Trait("Req", "FR-VIEW-02")]
public sealed class FR_VIEW_02_TimerRulesTests
{
    [Theory]
    [Trait("Req", "AC-23")]
    [InlineData("Bake for 25 minutes until golden.", "25 minutes", 1500, null)]
    [InlineData("Simmer 10 to 12 min.", "10 to 12 min", 600, 720)]
    [InlineData("Simmer 10–12 minutes.", "10–12 minutes", 600, 720)]
    [InlineData("Roast 1½ hours.", "1½ hours", 5400, null)]
    [InlineData("Roast 1 1/2 hrs.", "1 1/2 hrs", 5400, null)]
    [InlineData("Rest 1 hour 15 minutes.", "1 hour 15 minutes", 4500, null)]
    [InlineData("Rest 1 hour and 15 min.", "1 hour and 15 min", 4500, null)]
    [InlineData("Whisk 30 seconds.", "30 seconds", 30, null)]
    [InlineData("Microwave ½ minute.", "½ minute", 30, null)]
    [InlineData("Cook 2.5 minutes.", "2.5 minutes", 150, null)]
    [InlineData("Cook 2,5 min.", "2,5 min", 150, null)]
    [InlineData("Wait 3/4 hour.", "3/4 hour", 2700, null)]
    public void Durations_become_timers(string text, string match, int seconds, int? max)
    {
        var timer = TimerRules.Find(text).ShouldHaveSingleItem();
        timer.Text.ShouldBe(match);
        text.Substring(timer.Start, timer.Length).ShouldBe(match);
        timer.Seconds.ShouldBe(seconds);
        timer.SecondsMax.ShouldBe(max);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Cut into 2 m strips.")]
    [InlineData("Add 3 s of salt.")]
    [InlineData("Proof 2 h.")]
    [InlineData("Ferment 30 hours.")]
    [InlineData("0 minutes")]
    [InlineData("Stir in 2 cups flour.")]
    public void Other_text_has_no_timer(string? text) => TimerRules.Find(text).ShouldBeEmpty();

    [Fact]
    public void Several_timers_in_one_step_and_a_later_unit_does_not_add_a_larger_one()
    {
        var timers = TimerRules.Find("Bake 20 minutes, rotate, then bake 5 minutes more.");
        timers.Select(t => t.Seconds).ShouldBe([1200, 300]);
        TimerRules.Find("Rest 5 minutes 1 hour")[0].Seconds.ShouldBe(300);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(-5, "0:00")]
    [InlineData(90, "1:30")]
    [InlineData(1500, "25:00")]
    [InlineData(3900, "1:05:00")]
    public void Format_shows_minutes_or_hours(int seconds, string text) => TimerRules.Format(seconds).ShouldBe(text);
}
