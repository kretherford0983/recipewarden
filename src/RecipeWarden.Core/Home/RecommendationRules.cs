namespace RecipeWarden.Core.Home;

/// <summary>Which half of the world the household is in; it decides which season a month falls in (ADR 0044).</summary>
public enum Hemisphere
{
    North,
    South,
}

/// <summary>The home page's recommendation rows, in the order they are shown.</summary>
public enum RecommendationKind
{
    InSeason,
    Favorites,
    NotCookedLately,
}

/// <summary>What the rules need to know about one published recipe.</summary>
public sealed record RecommendationCandidate(int Id, int? Rating, DateTimeOffset? LastCookedAt, DateTimeOffset CreatedAt, IReadOnlyCollection<string> TagNames);

/// <summary>
/// FR-HOME-02, ADR 0044: rule-based recommendations. In season: a season tag for the current month (Holiday in
/// November and December). Favorites: rated 5. Not cooked lately: rated 4 or more and not cooked for 60 days or
/// never cooked. "Recently added" is the existing FR-HOME-01 row. A recipe appears in one row only, the first that
/// fits; each row holds at most <see cref="RowSize"/> and all rows together at most <see cref="MaxTotal"/> (AC-60),
/// picked the same way all day so the page does not jump.
/// </summary>
public static class RecommendationRules
{
    public const int RowSize = 4;
    public const int MaxTotal = 8;
    public const int HighRating = 4;
    public const int FavoriteRating = 5;
    public static readonly TimeSpan NotCookedFor = TimeSpan.FromDays(60);

    /// <summary>Normalized tag names (see NameRules.Normalize) that count as "in season" for the month.</summary>
    public static IReadOnlySet<string> SeasonTags(int month, Hemisphere hemisphere)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        var shifted = hemisphere == Hemisphere.South ? ((month + 5) % 12) + 1 : month;
        var tags = new HashSet<string>(StringComparer.Ordinal);
        switch (shifted)
        {
            case 3 or 4 or 5:
                tags.Add("spring");
                break;
            case 6 or 7 or 8:
                tags.Add("summer");
                break;
            case 9 or 10 or 11:
                tags.Add("fall");
                tags.Add("autumn");
                break;
            default:
                tags.Add("winter");
                break;
        }

        if (month is 11 or 12)
        {
            tags.Add("holiday");
        }

        return tags;
    }

    public static bool IsFavorite(int? rating) => rating >= FavoriteRating;

    public static bool IsDue(int? rating, DateTimeOffset? lastCookedAt, DateTimeOffset now) =>
        rating >= HighRating && (lastCookedAt is null || now - lastCookedAt.Value >= NotCookedFor);

    /// <summary>Recipe ids for each row. Tag names must already be normalized.</summary>
    public static IReadOnlyDictionary<RecommendationKind, IReadOnlyList<int>> Pick(
        IEnumerable<RecommendationCandidate> candidates, DateTimeOffset now, Hemisphere hemisphere)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var season = SeasonTags(now.Month, hemisphere);
        var day = now.Year * 1000 + now.DayOfYear;
        var used = new HashSet<int>();
        var rows = new Dictionary<RecommendationKind, IReadOnlyList<int>>();
        var all = candidates.ToList();

        rows[RecommendationKind.InSeason] = Take(all.Where(c => c.TagNames.Any(season.Contains)), day, used, byRating: true);
        rows[RecommendationKind.Favorites] = Take(all.Where(c => IsFavorite(c.Rating)), day, used, byRating: false);
        rows[RecommendationKind.NotCookedLately] = Take(all.Where(c => IsDue(c.Rating, c.LastCookedAt, now)), day, used, byRating: true,
            oldestFirst: true);
        return rows;
    }

    private static List<int> Take(IEnumerable<RecommendationCandidate> fits, int day, HashSet<int> used, bool byRating, bool oldestFirst = false)
    {
        var ordered = fits.Where(c => !used.Contains(c.Id));
        ordered = oldestFirst
            ? ordered.OrderBy(c => c.LastCookedAt ?? DateTimeOffset.MinValue).ThenByDescending(c => c.Rating ?? 0)
            : byRating
                ? ordered.OrderByDescending(c => c.Rating ?? 0).ThenBy(c => Shuffle(c.Id, day))
                : ordered.OrderBy(c => Shuffle(c.Id, day));
        var picked = ordered.Take(Math.Min(RowSize, MaxTotal - used.Count)).Select(c => c.Id).ToList();
        used.UnionWith(picked);
        return picked;
    }

    /// <summary>A stable per-day order, so equal candidates rotate from day to day without randomness in tests.</summary>
    private static uint Shuffle(int id, int day)
    {
        unchecked
        {
            var x = (uint)id * 2654435761u ^ (uint)day * 40503u;
            x ^= x >> 15;
            x *= 2246822519u;
            x ^= x >> 13;
            return x;
        }
    }
}
