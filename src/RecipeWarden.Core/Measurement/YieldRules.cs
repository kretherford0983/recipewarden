using System.Globalization;

namespace RecipeWarden.Core.Measurement;

/// <summary>
/// BR-26, FR-MEAS-04: a prepared-state amount ("4 cups cooked shredded chicken") shown as the purchase amount, always
/// as an "about" range with its basis, never as an exact figure.
/// </summary>
public static class YieldRules
{
    private const decimal GramsPerPound = 453.59237m;
    private const decimal GramsPerOunce = 28.349523125m;

    /// <summary>"About 1½–1¾ lb raw chicken breast to buy (boneless breast, simmered, about 70–88% cooking yield)."</summary>
    public static string Note(decimal lowGrams, decimal highGrams, bool metric, string toState, string food, string basis)
    {
        var range = metric ? MetricRange(lowGrams, highGrams) : UsRange(lowGrams, highGrams);
        return "About " + range + " " + toState + " " + food + " to buy (" + basis + ").";
    }

    /// <summary>Pounds to the nearest quarter at 1 lb and over; whole ounces below.</summary>
    public static string UsRange(decimal low, decimal high)
    {
        if (high >= GramsPerPound)
        {
            var a = Quarter(low / GramsPerPound);
            var b = Quarter(high / GramsPerPound);
            return Join(Fractions.Format(a), Fractions.Format(b), a == b) + " lb";
        }

        var oa = Math.Max(1m, Math.Round(low / GramsPerOunce, MidpointRounding.AwayFromZero));
        var ob = Math.Max(1m, Math.Round(high / GramsPerOunce, MidpointRounding.AwayFromZero));
        return Join(Fractions.Format(oa), Fractions.Format(ob), oa == ob) + " oz";
    }

    /// <summary>Grams to the nearest 10 g, or kilograms to 0.05 kg from 1 kg.</summary>
    public static string MetricRange(decimal low, decimal high)
    {
        if (high >= 1000m)
        {
            var a = Math.Round(low / 1000m * 20m, MidpointRounding.AwayFromZero) / 20m;
            var b = Math.Round(high / 1000m * 20m, MidpointRounding.AwayFromZero) / 20m;
            return Join(a.ToString("0.##", CultureInfo.InvariantCulture), b.ToString("0.##", CultureInfo.InvariantCulture), a == b) + " kg";
        }

        var ga = Math.Max(10m, Math.Round(low / 10m, MidpointRounding.AwayFromZero) * 10m);
        var gb = Math.Max(10m, Math.Round(high / 10m, MidpointRounding.AwayFromZero) * 10m);
        return Join(ga.ToString("0", CultureInfo.InvariantCulture), gb.ToString("0", CultureInfo.InvariantCulture), ga == gb) + " g";
    }

    private static decimal Quarter(decimal value) => Math.Max(0.25m, Math.Round(value * 4m, MidpointRounding.AwayFromZero) / 4m);

    private static string Join(string a, string b, bool same) => same ? a : a + "–" + b;
}
