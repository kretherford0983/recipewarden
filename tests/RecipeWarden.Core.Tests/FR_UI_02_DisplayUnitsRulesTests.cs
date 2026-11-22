using RecipeWarden.Core.Measurement;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-UI-02, ADR 0014: unit preference from the query string, profile or cookie.</summary>
[Trait("Req", "FR-UI-02")]
public sealed class FR_UI_02_DisplayUnitsRulesTests
{
    [Theory]
    [InlineData("us", DisplayUnits.US)]
    [InlineData("METRIC", DisplayUnits.Metric)]
    [InlineData("weight", DisplayUnits.Weight)]
    [InlineData("as-written", DisplayUnits.AsWritten)]
    [InlineData("bogus", DisplayUnits.AsWritten)]
    [InlineData(null, DisplayUnits.AsWritten)]
    public void Parse(string? value, DisplayUnits expected)
    {
        DisplayUnitsRules.Parse(value).ShouldBe(expected);
    }

    [Theory]
    [InlineData(DisplayUnits.AsWritten)]
    [InlineData(DisplayUnits.US)]
    [InlineData(DisplayUnits.Metric)]
    [InlineData(DisplayUnits.Weight)]
    public void Values_round_trip(DisplayUnits units)
    {
        DisplayUnitsRules.Parse(DisplayUnitsRules.ToValue(units)).ShouldBe(units);
    }

    [Fact]
    public void Query_wins_then_profile_then_cookie()
    {
        DisplayUnitsRules.Resolve("metric", DisplayUnits.US, "weight").ShouldBe(DisplayUnits.Metric);
        DisplayUnitsRules.Resolve(null, DisplayUnits.US, "weight").ShouldBe(DisplayUnits.US);
        DisplayUnitsRules.Resolve("junk", null, "weight").ShouldBe(DisplayUnits.Weight);
        DisplayUnitsRules.Resolve(null, null, null).ShouldBe(DisplayUnits.AsWritten);
    }
}
