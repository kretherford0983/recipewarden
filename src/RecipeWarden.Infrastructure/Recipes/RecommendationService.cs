using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Home;
using RecipeWarden.Core.Organization;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Recipes;

/// <summary>ADR 0044: "Home:Hemisphere" (North or South) decides which season a month is in.</summary>
public sealed class HomeOptions
{
    public const string SectionName = "Home";

    public Hemisphere Hemisphere { get; set; } = Hemisphere.North;
}

public sealed record Recommendations(IReadOnlyList<RecipeCard> InSeason, IReadOnlyList<RecipeCard> Favorites, IReadOnlyList<RecipeCard> NotCookedLately)
{
    public bool IsEmpty => InSeason.Count == 0 && Favorites.Count == 0 && NotCookedLately.Count == 0;
}

/// <summary>FR-HOME-02, ADR 0044: the home page's recommendation rows, from published recipes only.</summary>
public sealed class RecommendationService(AppDbContext db, TimeProvider clock, HomeOptions options)
{
    public async Task<Recommendations> GetAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var season = RecommendationRules.SeasonTags(now.Month, options.Hemisphere).ToList();
        var since = now - RecommendationRules.NotCookedFor;

        // Only recipes that could fit a row are read: tagged for the season, or rated highly.
        var rows = await db.Recipes.AsNoTracking()
            .Where(r => r.Status == RecipeStatus.Published)
            .Where(r => r.Rating >= RecommendationRules.HighRating
                || r.Tags.Any(t => t.Tag != null && (t.Tag.TagType == null || t.Tag.TagType == TagType.Season) && season.Contains(t.Tag.NormalizedName)))
            .Select(r => new
            {
                r.Id, r.Rating, r.LastCookedAt, r.CreatedAt,
                Tags = r.Tags.Where(t => t.Tag != null && (t.Tag.TagType == null || t.Tag.TagType == TagType.Season)).Select(t => t.Tag!.NormalizedName).ToList(),
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var picked = RecommendationRules.Pick(rows.Select(r => new RecommendationCandidate(r.Id, r.Rating, r.LastCookedAt, r.CreatedAt, r.Tags)), now,
            options.Hemisphere);
        var ids = picked.Values.SelectMany(v => v).ToList();
        if (ids.Count == 0)
        {
            return new Recommendations([], [], []);
        }

        var cards = (await db.Recipes.AsNoTracking().Where(r => ids.Contains(r.Id))
                .Select(r => new { r.Id, r.Slug, r.Title, r.Description, r.TotalMinutes, r.TotalMinutesMax, r.PrepMinutes, r.PrepMinutesMax,
                    r.CookMinutes, r.CookMinutesMax, r.Rating, r.Status, r.FinishImageId })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(r => r.Id, r => RecipeSearchService.Card(r.Id, r.Slug, r.Title, r.Description, (r.TotalMinutes, r.TotalMinutesMax),
                (r.PrepMinutes, r.PrepMinutesMax), (r.CookMinutes, r.CookMinutesMax), r.Rating, r.Status, r.FinishImageId));

        List<RecipeCard> Row(RecommendationKind kind) => picked[kind].Where(cards.ContainsKey).Select(id => cards[id]).ToList();
        return new Recommendations(Row(RecommendationKind.InSeason), Row(RecommendationKind.Favorites), Row(RecommendationKind.NotCookedLately));
    }
}
