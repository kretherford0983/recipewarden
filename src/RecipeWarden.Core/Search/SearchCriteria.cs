using System.Globalization;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Search;

/// <summary>FR-SRCH-02 sort orders.</summary>
public enum SearchSort
{
    Relevance,
    Newest,
    Title,
    LastCooked,
}

/// <summary>Raw query-string values for <c>/search</c> and <c>/recipes</c>, as the browser sent them.</summary>
public sealed record SearchInput
{
    public string? Q { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public string? Collection { get; init; }
    public string? MaxMinutes { get; init; }
    public string? MinRating { get; init; }
    public string? Sort { get; init; }
    public string? Page { get; init; }
}

/// <summary>
/// FR-SRCH-01/02: checked search criteria. Every value is parsed against an allow-list or a range; anything else
/// is dropped, so nothing from the query string reaches SQL except as a typed parameter.
/// "Free of" allergens is M6 and "has image" is M4 (ADR 0009).
/// </summary>
public sealed record SearchCriteria
{
    public const int MaxTags = 10;
    public const int PageSize = 24;
    public const int MaxPage = 1000;
    public const int MaxMinutesLimit = 24 * 60;

    private static readonly Dictionary<string, SearchSort> Sorts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["relevance"] = SearchSort.Relevance,
        ["newest"] = SearchSort.Newest,
        ["title"] = SearchSort.Title,
        ["lastcooked"] = SearchSort.LastCooked,
    };

    public FtsQuery Query { get; init; } = FtsQuery.Empty;
    public IReadOnlyList<string> TagSlugs { get; init; } = [];
    public string? CollectionSlug { get; init; }
    public int? MaxTotalMinutes { get; init; }
    public int? MinRating { get; init; }
    public SearchSort Sort { get; init; } = SearchSort.Newest;
    public int Page { get; init; } = 1;

    public int Skip => (Page - 1) * PageSize;

    public static SearchCriteria Parse(SearchInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var query = FtsQuery.Build(input.Q);
        var sort = input.Sort is not null && Sorts.TryGetValue(input.Sort, out var s) ? s
            : query.IsEmpty ? SearchSort.Newest : SearchSort.Relevance;
        if (sort == SearchSort.Relevance && query.IsEmpty)
        {
            sort = SearchSort.Newest;
        }

        return new SearchCriteria
        {
            Query = query,
            TagSlugs = (input.Tags ?? []).Where(SlugRules.IsValid).Distinct(StringComparer.Ordinal).Take(MaxTags).ToArray(),
            CollectionSlug = SlugRules.IsValid(input.Collection) ? input.Collection : null,
            MaxTotalMinutes = InRange(input.MaxMinutes, 1, MaxMinutesLimit),
            MinRating = InRange(input.MinRating, 1, 5),
            Sort = sort,
            Page = Math.Clamp(ParseWhole(input.Page) ?? 1, 1, MaxPage),
        };
    }

    private static int? InRange(string? text, int min, int max) =>
        ParseWhole(text) is { } n && n >= min && n <= max ? n : null;

    private static int? ParseWhole(string? text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
}
