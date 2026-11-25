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

/// <summary>BR-49: one row of the kitchen-location editor. <see cref="Original"/> is null for a new location.</summary>
public sealed record LocationEdit(string? Original, string? Name, bool Remove = false);

/// <summary>FR-SHOP-05, BR-47, BR-49: staples' kitchen locations, checks and wording.</summary>
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

    /// <summary>ADR 0057: the starting kitchen locations, in the order a walk around the kitchen visits them (BR-49: editable).</summary>
    public static IReadOnlyList<string> Locations { get; } = ["Fridge", "Freezer", "Pantry", "Spice rack", "Under the sink", "Other"];

    public const string DefaultLocation = "Pantry";

    /// <summary>BR-49: the catch-all location; always last, never renamed or removed.</summary>
    public const string OtherLocation = "Other";

    public const int MaxLocations = 20;
    public const int MaxLocationLength = 24;

    public const string LocationRequired = "Give each location a name.";
    public const string LocationTooLong = "Keep a location's name under 24 characters.";
    public const string LocationTwice = "Each location can be listed once.";
    public const string TooManyLocations = "Keep the list to 20 locations.";

    /// <summary>Where a staple goes when nothing says otherwise: Pantry when it exists, else Other.</summary>
    public static string DefaultFor(IReadOnlyList<string> locations)
    {
        ArgumentNullException.ThrowIfNull(locations);
        return locations.Contains(DefaultLocation, StringComparer.Ordinal) ? DefaultLocation : OtherLocation;
    }

    /// <summary>The stored list (one per line), or the starting list. "Other" is always there, last.</summary>
    public static IReadOnlyList<string> ParseLocations(string? stored)
    {
        var list = (stored ?? string.Empty).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)
            .Where(l => !string.Equals(l, OtherLocation, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxLocations - 1).ToList();
        return list.Count == 0 && string.IsNullOrWhiteSpace(stored) ? Locations : [.. list, OtherLocation];
    }

    /// <summary>
    /// BR-49: applies the location editor's rows (in their new order). A renamed location moves its staples with it; a
    /// removed one moves them to "Other". Returns the new list, the moves (old → new) and an error, if any.
    /// </summary>
    public static (IReadOnlyList<string> Locations, IReadOnlyDictionary<string, string> Moves, string? Error) EditLocations(
        IReadOnlyList<string> current, IReadOnlyList<LocationEdit> rows)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(rows);
        var moves = new Dictionary<string, string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var row in rows)
        {
            var original = row.Original is { } o && current.Contains(o, StringComparer.Ordinal) && o != OtherLocation ? o : null;
            var name = (row.Name ?? string.Empty).Trim();
            if (original is null && name.Length == 0)
            {
                continue; // the empty "add a location" row
            }

            if (row.Remove)
            {
                if (original is not null)
                {
                    moves[original] = OtherLocation;
                }

                continue;
            }

            if (name.Length == 0)
            {
                return (current, moves, LocationRequired);
            }

            if (name.Length > MaxLocationLength)
            {
                return (current, moves, LocationTooLong);
            }

            if (string.Equals(name, OtherLocation, StringComparison.OrdinalIgnoreCase) || result.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                return (current, moves, LocationTwice);
            }

            if (original is not null && original != name)
            {
                moves[original] = name;
            }

            result.Add(name);
        }

        if (result.Count + 1 > MaxLocations)
        {
            return (current, moves, TooManyLocations);
        }

        return ([.. result, OtherLocation], moves, null);
    }

    public static string? Validate(string? name, string? location, string? restock, string? section, IReadOnlyList<string>? locations = null)
    {
        locations ??= Locations;
        if (string.IsNullOrWhiteSpace(name))
        {
            return NameRequired;
        }

        if (name.Trim().Length > MaxNameLength)
        {
            return NameTooLong;
        }

        if (location is null || !locations.Contains(location, StringComparer.Ordinal))
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
    public static IReadOnlyList<IGrouping<string, StapleInfo>> ByLocation(IEnumerable<StapleInfo> staples, IReadOnlyList<string>? locations = null)
    {
        ArgumentNullException.ThrowIfNull(staples);
        locations ??= Locations;
        return staples.OrderBy(s => Rank(s.Location, locations)).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
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

    private static int Rank(string location, IReadOnlyList<string> locations)
    {
        for (var i = 0; i < locations.Count; i++)
        {
            if (string.Equals(locations[i], location, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return locations.Count;
    }
}
