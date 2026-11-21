using System.Globalization;
using System.Text.RegularExpressions;

namespace RecipeWarden.Core.Recipes;

/// <summary>A time typed in the editor, in minutes: a value, a range, or nothing.</summary>
public sealed record TimeValue(int? Min, int? Max, string? Error);

/// <summary>
/// ADR 0010: prep, cook and total times. Accepts minutes ("20"), ranges ("20-25", "20 to 25 min"), hours and
/// minutes ("1 hr 15 min", "1h15m", "1:15") and hour ranges ("1-1.5 hours"). A unitless low end takes the high end's
/// unit ("1-1.5 hours" is 60–90 minutes). Stored as whole minutes, at most 7 days.
/// </summary>
public static partial class TimeText
{
    public const int MaxMinutes = 7 * 24 * 60;
    public const string Unreadable = "Enter a time like 20, 20-25, 1 hr 15 min or 1-1.5 hours";
    public const string HighBelowLow = "The second time of a range must be longer than the first";
    public const string HighWithoutLow = "A range needs a first time";
    public const string TooLong = "Times must be between 0 minutes and 7 days";

    private static readonly Dictionary<string, int> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        ["m"] = 1, ["min"] = 1, ["mins"] = 1, ["minute"] = 1, ["minutes"] = 1,
        ["h"] = 60, ["hr"] = 60, ["hrs"] = 60, ["hour"] = 60, ["hours"] = 60,
        ["d"] = 1440, ["day"] = 1440, ["days"] = 1440,
    };

    [GeneratedRegex(@"\b(?:about|approx\.?|approximately|around)\b|~", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex Words();

    [GeneratedRegex(@"\s*(?:–|—|-|\bto\b)\s*", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex RangeSeparator();

    [GeneratedRegex(@"^(\d{1,3}):([0-5]\d)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Clock();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*([a-z]*)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex Part();

    public static TimeValue Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new TimeValue(null, null, null);
        }

        var cleaned = Words().Replace(text, " ").Trim();
        var sides = RangeSeparator().Split(cleaned);
        if (sides.Length > 2 || sides.Any(s => s.Length == 0))
        {
            return new TimeValue(null, null, Unreadable);
        }

        var highUnit = sides.Length == 2 ? FirstUnit(sides[1]) : null;
        var low = ParseSide(sides[0], highUnit);
        var high = sides.Length == 2 ? ParseSide(sides[1], null) : null;
        if (low is null || (sides.Length == 2 && high is null))
        {
            return new TimeValue(null, null, Unreadable);
        }

        int? max = high == low ? null : high;
        var error = ValidateRange(low, max);
        return error is null ? new TimeValue(low, max, null) : new TimeValue(null, null, error);
    }

    public static string? ValidateRange(int? min, int? max)
    {
        if (min is null)
        {
            return max is null ? null : HighWithoutLow;
        }

        if (min < 0 || min > MaxMinutes || max > MaxMinutes)
        {
            return TooLong;
        }

        return max is { } high && high <= min ? HighBelowLow : null;
    }

    /// <summary>"20 min", "20–25 min", "1 hr 15 min", "1 hr – 1 hr 30 min". Empty when there is no time.</summary>
    public static string Format(int? min, int? max)
    {
        if (min is not { } low)
        {
            return string.Empty;
        }

        if (max is not { } high)
        {
            return One(low);
        }

        return low < 60 && high < 60
            ? string.Create(CultureInfo.InvariantCulture, $"{low}–{high} min")
            : One(low) + " – " + One(high);
    }

    /// <summary>Total from prep and cook when no total was given; the high ends add when either side is a range.</summary>
    public static (int? Min, int? Max) Sum(int? prep, int? prepMax, int? cook, int? cookMax)
    {
        if (prep is null && cook is null)
        {
            return (null, null);
        }

        var min = (prep ?? 0) + (cook ?? 0);
        int? max = prepMax is null && cookMax is null ? null : (prepMax ?? prep ?? 0) + (cookMax ?? cook ?? 0);
        return (min, max);
    }

    private static string One(int minutes)
    {
        var h = minutes / 60;
        var m = minutes % 60;
        return h == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{m} min")
            : m == 0
                ? string.Create(CultureInfo.InvariantCulture, $"{h} hr")
                : string.Create(CultureInfo.InvariantCulture, $"{h} hr {m} min");
    }

    private static string? FirstUnit(string side)
    {
        foreach (Match p in Part().Matches(side))
        {
            if (p.Groups[2].Value.Length > 0)
            {
                return p.Groups[2].Value;
            }
        }

        return null;
    }

    /// <summary>One side of a range in minutes, or null if unreadable.</summary>
    private static int? ParseSide(string side, string? unitIfMissing)
    {
        var clock = Clock().Match(side);
        if (clock.Success)
        {
            return (int.Parse(clock.Groups[1].Value, CultureInfo.InvariantCulture) * 60)
                + int.Parse(clock.Groups[2].Value, CultureInfo.InvariantCulture);
        }

        var parts = Part().Matches(side);
        if (parts.Count == 0 || string.Concat(parts.Select(p => p.Value)).Replace(" ", string.Empty, StringComparison.Ordinal)
                != side.Replace(" ", string.Empty, StringComparison.Ordinal))
        {
            return null; // text that is not number-and-unit pairs
        }

        decimal total = 0;
        for (var i = 0; i < parts.Count; i++)
        {
            var unit = parts[i].Groups[2].Value;
            if (unit.Length == 0)
            {
                // A bare number: the shared unit for a lone low end ("1-1.5 hours"), else minutes ("1 hr 15").
                unit = parts.Count == 1 && unitIfMissing is not null ? unitIfMissing : "min";
            }

            if (!Units.TryGetValue(unit, out var factor)
                || !decimal.TryParse(parts[i].Groups[1].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            {
                return null;
            }

            total += value * factor;
        }

        return total > int.MaxValue ? null : (int)Math.Round(total, MidpointRounding.AwayFromZero);
    }
}
