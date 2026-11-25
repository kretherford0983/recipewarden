using System.Globalization;
using System.Text.RegularExpressions;

namespace RecipeWarden.Core.Recipes;

/// <summary>A duration found in a step: where it is in the text, and how long (the low end of a range).</summary>
public sealed record StepTimer(int Start, int Length, string Text, int Seconds, int? SecondsMax);

/// <summary>
/// FR-VIEW-02 (cook mode), ADR 0031: finds durations in a step's text so cook mode can offer a timer
/// ("bake 25 minutes", "10 to 12 min", "1½ hours", "1 hour 15 minutes", "30 seconds"). Ranges start at the low end.
/// Only plain text is read (BR-13); nothing longer than 24 hours is offered.
/// </summary>
public static partial class TimerRules
{
    public const int MaxSeconds = 24 * 3600;

    private const string Number = @"\d+(?:[.,]\d+)?(?:\s*[½⅓⅔¼¾])?|[½⅓⅔¼¾]|\d+\s+\d/\d|\d/\d";

    [GeneratedRegex(
        @"(?<!\w)(?<a>" + Number + @")(?:\s*(?:-|–|to)\s*(?<b>" + Number + @"))?\s*(?<u>hours?|hrs?|h|minutes?|mins?|m|seconds?|secs?|s)\b" +
        @"(?:\s*(?:and\s*)?(?<a2>\d+)\s*(?<u2>minutes?|mins?|m|seconds?|secs?|s)\b)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex DurationPattern();

    public static IReadOnlyList<StepTimer> Find(string? text)
    {
        var result = new List<StepTimer>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        try
        {
            foreach (Match m in DurationPattern().Matches(text))
            {
                var unit = m.Groups["u"].Value.ToLowerInvariant();
                // A bare "m" or "s" is too easy to misread ("2 m" of cloth); require a word or "min"/"sec"/"hr".
                if (unit is "m" or "s" or "h")
                {
                    continue;
                }

                var factor = UnitSeconds(unit);
                var low = ParseNumber(m.Groups["a"].Value);
                if (low is null || low <= 0)
                {
                    continue;
                }

                decimal? high = m.Groups["b"].Success ? ParseNumber(m.Groups["b"].Value) : null;
                var seconds = low.Value * factor;
                decimal? secondsMax = high is { } h && h > low ? h * factor : null;

                if (m.Groups["a2"].Success && high is null)
                {
                    var extra = decimal.Parse(m.Groups["a2"].Value, CultureInfo.InvariantCulture) * UnitSeconds(m.Groups["u2"].Value.ToLowerInvariant());
                    if (UnitSeconds(m.Groups["u2"].Value.ToLowerInvariant()) < factor)
                    {
                        seconds += extra;
                    }
                }

                var s = (int)Math.Round(seconds);
                if (s < 1 || s > MaxSeconds)
                {
                    continue;
                }

                var max = secondsMax is { } sm ? (int)Math.Min(Math.Round(sm), MaxSeconds) : (int?)null;
                result.Add(new StepTimer(m.Index, m.Length, m.Value, s, max));
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // A pathological step: no timers rather than a hang.
        }

        return result;
    }

    /// <summary>"25:00", "1:05:00".</summary>
    public static string Format(int seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{t.Minutes}:{t.Seconds:00}");
    }

    private static decimal UnitSeconds(string unit) => unit[0] switch
    {
        'h' => 3600m,
        'm' => 60m,
        _ => 1m,
    };

    private static decimal? ParseNumber(string text)
    {
        text = text.Trim().Replace(',', '.');
        decimal total = 0;
        var vulgar = text[^1] switch
        {
            '½' => 0.5m,
            '⅓' => 1m / 3,
            '⅔' => 2m / 3,
            '¼' => 0.25m,
            '¾' => 0.75m,
            _ => (decimal?)null,
        };
        if (vulgar is { } v)
        {
            total += v;
            text = text[..^1].Trim();
            if (text.Length == 0)
            {
                return total;
            }
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var slash = part.IndexOf('/', StringComparison.Ordinal);
            if (slash > 0)
            {
                if (!decimal.TryParse(part[..slash], NumberStyles.Number, CultureInfo.InvariantCulture, out var n)
                    || !decimal.TryParse(part[(slash + 1)..], NumberStyles.Number, CultureInfo.InvariantCulture, out var d) || d == 0)
                {
                    return null;
                }

                total += n / d;
            }
            else if (decimal.TryParse(part, NumberStyles.Number, CultureInfo.InvariantCulture, out var whole))
            {
                total += whole;
            }
            else
            {
                return null;
            }
        }

        return total;
    }
}
