using RecipeWarden.Core.Measurement;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-MEAS-07 (°F/°C part; pan sizes in M3, ADR 0012).</summary>
[Trait("Req", "FR-MEAS-07")]
public sealed class FR_MEAS_07_TemperatureTextTests
{
    [Theory]
    [InlineData("Preheat oven to 375°F.", "Preheat oven to 190°C.")]
    [InlineData("Bake at 350 degrees F for 25 minutes", "Bake at 175°C for 25 minutes")]
    [InlineData("Heat to 425 °F", "Heat to 220°C")]
    [InlineData("Roast at 400 degrees Fahrenheit", "Roast at 205°C")]
    [InlineData("Cook 1 cup rice for 18 minutes", "Cook 1 cup rice for 18 minutes")]
    [InlineData("Heat to 200°C", "Heat to 200°C")]
    public void Metric_shows_celsius(string text, string expected)
    {
        TemperatureText.Convert(text, DisplayUnits.Metric).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Bake at 200°C.", "Bake at 390°F.")]
    [InlineData("Bake at 180 degrees C", "Bake at 355°F")]
    [InlineData("Bake at 350°F", "Bake at 350°F")]
    public void Us_shows_fahrenheit(string text, string expected)
    {
        TemperatureText.Convert(text, DisplayUnits.US).ShouldBe(expected);
    }

    [Theory]
    [InlineData(DisplayUnits.AsWritten)]
    [InlineData(DisplayUnits.Weight)]
    public void As_written_and_weight_leave_text_alone(DisplayUnits units)
    {
        TemperatureText.Convert("Bake at 350°F", units).ShouldBe("Bake at 350°F");
    }

    [Fact]
    public void Null_is_empty()
    {
        TemperatureText.Convert(null, DisplayUnits.Metric).ShouldBe(string.Empty);
    }
}
