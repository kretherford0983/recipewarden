using System.Globalization;

namespace RecipeWarden.Core.Shopping;

/// <summary>
/// FR-SHOP-05 (ADR 0057): one staple as the walk-through sees it. <see cref="FoodId"/> is null for a non-food staple
/// (foil, dish soap). <see cref="Restock"/> is the usual amount ("1 dozen", "5 lb"), read like an ingredient line.
/// </summary>
public sealed record StapleInfo(
    int Id,
    int? FoodId,
    string Name,
    string Location,
    string? Restock,
    string? Section,
    DateTimeOffset? LastRestockedAt);

/// <summary>FR-SHOP-05, BR-47: staples' kitchen locations, checks and wording.</summary>
public static class StapleRules
{
    public const int MaxNameLength = 80;
    public const int MaxRestockLength = 40;
    public const int MaxStaples = 300;

    public const string NameRequired = "Give the staple a name, e.g. eggs or aluminum foil.";
    public const string NameTooLong = "Keep the name under 80 characters.";
    public const string RestockTooLong = "Keep the usual amount short, e.g. 1 dozen or 5 lb.";
    public const string BadLocation = "Choose where it lives from the list.";
    public const string AlreadyAStaple = "That is already a staple.";
    public const string TooMany = "The staples list is full.";

    /// <summary>ADR 0057: where staples live, in the order a walk around the kitchen visits them.</summary>
    public static IReadOnlyList<string> Locations { get; } = ["Fridge", "Freezer", "Pantry", "Spice rack", "Under the sink", "Other"];

    public const string DefaultLocation = "Pantry";

    public static string? Validate(string? name, string? location, string? restock, string? section)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return NameRequired;
        }

        if (name.Trim().Length > MaxNameLength)
        {
            return NameTooLong;
        }

        if (location is null || !Locations.Contains(location, StringComparer.Ordinal))
        {
            return BadLocation;
        }

        if ((restock?.Trim().Length ?? 0) > MaxRestockLength)
        {
            return RestockTooLong;
        }

        return section is null || ShoppingRules.SectionOrder.Contains(section, StringComparer.Ordinal) ? null : ShoppingRules.BadSection;
    }

    /// <summary>BR-45: the usual amount as the ingredient reader takes it: "1 dozen" → "12", "half a dozen" → "6".</summary>
    public static string RestockAmount(string restock)
    {
        ArgumentNullException.ThrowIfNull(restock);
        var t = restock.Trim();
        var words = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2 && words[^1].Equals("dozen", StringComparison.OrdinalIgnoreCase))
        {
            var count = string.Join(' ', words[..^1]).ToLowerInvariant() switch
            {
                "a" or "one" => 1m,
                "half a" or "half" or "a half" => 0.5m,
                "two" => 2m,
                var n when decimal.TryParse(n, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d) && d > 0 => d,
                _ => (decimal?)null,
            };
            if (count is { } c)
            {
                return (c * 12).ToString("0.##", CultureInfo.InvariantCulture);
            }
        }

        return t;
    }

    /// <summary>The walk-through's order: by location (fixed order), then name.</summary>
    public static IReadOnlyList<IGrouping<string, StapleInfo>> ByLocation(IEnumerable<StapleInfo> staples)
    {
        ArgumentNullException.ThrowIfNull(staples);
        return staples.OrderBy(s => Rank(s.Location)).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .GroupBy(s => s.Location, StringComparer.Ordinal).ToList();
    }

    /// <summary>BR-47: "last bought today", "last bought 3 weeks ago", or null when never.</summary>
    public static string? LastBought(DateTimeOffset? at, DateTimeOffset now)
    {
        if (at is not { } when)
        {
            return null;
        }

        var days = (int)Math.Floor((now - when).TotalDays);
        return days switch
        {
            < 1 => "last bought today",
            1 => "last bought yesterday",
            < 14 => "last bought " + days.ToString(CultureInfo.InvariantCulture) + " days ago",
            < 60 => "last bought " + (days / 7).ToString(CultureInfo.InvariantCulture) + " weeks ago",
            _ => "last bought " + (days / 30).ToString(CultureInfo.InvariantCulture) + " months ago",
        };
    }

    private static int Rank(string location)
    {
        for (var i = 0; i < Locations.Count; i++)
        {
            if (string.Equals(Locations[i], location, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return Locations.Count;
    }
}
