using System.Globalization;
using System.Text.RegularExpressions;

namespace RecipeWarden.Core.Measurement;

/// <summary>
/// FR-MEAS-07 (M2 part, ADR 0012): oven and cooking temperatures in step text shown in °F or °C by the reader's
/// unit choice. "375°F", "375 °F", "375 degrees F" and "375 degrees Fahrenheit" are recognised; converted values
/// round to the nearest 5 degrees, as oven dials do. As written and Weight leave the text alone.
/// </summary>
public static partial class TemperatureText
{
    [GeneratedRegex(@"(?<n>\d{2,3})\s*(?:°|º|degrees?\s*)\s*(?<u>Fahrenheit|Celsius|F|C)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Temperature();

    public static string Convert(string? text, DisplayUnits units)
    {
        if (string.IsNullOrEmpty(text) || units is not (DisplayUnits.US or DisplayUnits.Metric))
        {
            return text ?? string.Empty;
        }

        var toCelsius = units == DisplayUnits.Metric;
        return Temperature().Replace(text, m =>
        {
            var isFahrenheit = char.ToUpperInvariant(m.Groups["u"].Value[0]) == 'F';
            if (isFahrenheit != toCelsius)
            {
                return m.Value;
            }

            var n = decimal.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
            var converted = toCelsius ? (n - 32m) * 5m / 9m : n * 9m / 5m + 32m;
            var rounded = Math.Round(converted / 5m, MidpointRounding.AwayFromZero) * 5m;
            return rounded.ToString("0", CultureInfo.InvariantCulture) + (toCelsius ? "°C" : "°F");
        });
    }
}
