using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Nutrition;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Core.Search;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Recipes;

/// <summary>Which recipes a listing shows: published ones (everyone) or drafts (editors, ADR 0009).</summary>
public enum RecipeVisibility
{
    Published,
    Drafts,
}

/// <summary>
/// FR-SRCH-01/02, SEC-21, AC-21/22: full-text search and browse.
/// The FTS5 expression comes only from <see cref="FtsQuery"/> (quoted terms) and is passed as a parameter.
/// Ranking (AC-21): recipes matching more of the search words come first, then by bm25 with field weights
/// (title 10, tags 5, ingredients 4, description 2, steps 1, notes 1), then by title. Filters are LINQ.
/// </summary>
public sealed class RecipeSearchService(AppDbContext db)
{
    private const int SummaryLength = 160;

    private sealed class FtsHit
    {
        public long Id { get; set; }
        public double Score { get; set; }
    }

    public async Task<SearchPage> SearchAsync(SearchCriteria criteria, RecipeVisibility visibility = RecipeVisibility.Published,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var status = visibility == RecipeVisibility.Drafts ? RecipeStatus.Draft : RecipeStatus.Published;
        var query = db.Recipes.AsNoTracking().Where(r => r.Status == status);

        Dictionary<int, (int Matched, double Score)>? ranking = null;
        if (!criteria.Query.IsEmpty)
        {
            if (visibility == RecipeVisibility.Drafts)
            {
                // Drafts are not indexed; match the title instead so editors can still find one.
                var words = criteria.Query.Terms;
                foreach (var word in words)
                {
                    // Terms hold only letters and digits (FtsQuery), so they contain no LIKE wildcards.
                    var pattern = "%" + word + "%";
                    query = query.Where(r => EF.Functions.Like(r.Title, pattern));
                }
            }
            else
            {
                ranking = await RankAsync(criteria.Query, cancellationToken).ConfigureAwait(false);
                var ids = ranking.Keys.ToList();
                query = query.Where(r => ids.Contains(r.Id));
            }
        }

        foreach (var slug in criteria.TagSlugs)
        {
            query = query.Where(r => r.Tags.Any(t => t.Tag!.Slug == slug));
        }

        if (criteria.CollectionSlug is { } collection)
        {
            query = query.Where(r => db.CollectionRecipes.Any(cr => cr.RecipeId == r.Id
                && db.Collections.Any(c => c.Id == cr.CollectionId && c.Slug == collection)));
        }

        if (criteria.MaxTotalMinutes is { } max)
        {
            // ADR 0010: the high end must fit, so "30 min or less" never lists a recipe that may take 35.
            query = query.Where(r =>
                (r.TotalMinutesMax ?? r.TotalMinutes
                    ?? ((r.PrepMinutesMax ?? r.PrepMinutes ?? 0) + (r.CookMinutesMax ?? r.CookMinutes ?? 0))) > 0
                && (r.TotalMinutesMax ?? r.TotalMinutes
                    ?? ((r.PrepMinutesMax ?? r.PrepMinutes ?? 0) + (r.CookMinutesMax ?? r.CookMinutes ?? 0))) <= max);
        }

        if (criteria.HasImage)
        {
            query = query.Where(r => r.FinishImageId != null);
        }

        if (criteria.MinRating is { } rating)
        {
            query = query.Where(r => r.Rating >= rating);
        }

        // BR-53, ADR 0036: a manual row decides; otherwise the detected one. Contains and May contain both exclude.
        foreach (var code in criteria.FreeOf)
        {
            query = query.Where(r =>
                !db.RecipeAllergens.Any(a => a.RecipeId == r.Id && a.Allergen == code && a.Origin == AllergenOrigin.Manual
                                             && a.Status != AllergenStatus.NotPresent)
                && !(db.RecipeAllergens.Any(a => a.RecipeId == r.Id && a.Allergen == code && a.Origin == AllergenOrigin.Auto
                                                 && a.Status != AllergenStatus.NotPresent)
                     && !db.RecipeAllergens.Any(a => a.RecipeId == r.Id && a.Allergen == code && a.Origin == AllergenOrigin.Manual)));
        }

        if (criteria.FreeOf.Count > 0 && !criteria.IncludeUnverified)
        {
            // Unverified: a line with an amount that matched no food, so nothing is known about it.
            query = query.Where(r => !db.RecipeIngredients.Any(i => i.RecipeId == r.Id && i.FoodId == null && i.Quantity != null));
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var pageCount = Math.Max(1, (total + SearchCriteria.PageSize - 1) / SearchCriteria.PageSize);

        List<int> pageIds;
        if (criteria.Sort == SearchSort.Relevance && ranking is not null)
        {
            var titles = await query.Select(r => new { r.Id, r.Title }).ToListAsync(cancellationToken).ConfigureAwait(false);
            pageIds = titles
                .OrderByDescending(r => ranking[r.Id].Matched)
                .ThenBy(r => ranking[r.Id].Score)
                .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
                .Skip(criteria.Skip).Take(SearchCriteria.PageSize)
                .Select(r => r.Id).ToList();
        }
        else
        {
            var ordered = criteria.Sort switch
            {
                SearchSort.Title => query.OrderBy(r => EF.Functions.Collate(r.Title, "NOCASE")).ThenBy(r => r.Id),
                SearchSort.LastCooked => query.OrderBy(r => r.LastCookedAt == null).ThenByDescending(r => r.LastCookedAt).ThenBy(r => r.Title),
                _ => query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id),
            };
            pageIds = await ordered.Skip(criteria.Skip).Take(SearchCriteria.PageSize).Select(r => r.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        var rows = await db.Recipes.AsNoTracking().Where(r => pageIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Slug, r.Title, r.Description, r.TotalMinutes, r.TotalMinutesMax, r.PrepMinutes, r.PrepMinutesMax,
                r.CookMinutes, r.CookMinutesMax, r.Rating, r.Status, r.FinishImageId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var byId = rows.ToDictionary(r => r.Id);
        var items = pageIds.Where(byId.ContainsKey).Select(id => byId[id])
            .Select(r => Card(r.Id, r.Slug, r.Title, r.Description, (r.TotalMinutes, r.TotalMinutesMax), (r.PrepMinutes, r.PrepMinutesMax),
                (r.CookMinutes, r.CookMinutesMax), r.Rating, r.Status, r.FinishImageId))
            .ToList();

        return new SearchPage(items, total, criteria.Page, pageCount);
    }

    /// <summary>Recently added published recipes (home page and empty search).</summary>
    public Task<SearchPage> RecentAsync(CancellationToken cancellationToken = default) =>
        SearchAsync(SearchCriteria.Parse(new SearchInput()), RecipeVisibility.Published, cancellationToken);

    private async Task<Dictionary<int, (int Matched, double Score)>> RankAsync(FtsQuery query, CancellationToken cancellationToken)
    {
        var expr = query.MatchAny;
        var hits = await db.Database
            .SqlQuery<FtsHit>($"SELECT rowid AS Id, bm25(RecipeSearch, 10.0, 2.0, 4.0, 1.0, 5.0, 1.0) AS Score FROM RecipeSearch WHERE RecipeSearch MATCH {expr}")
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var ranking = hits.ToDictionary(h => (int)h.Id, h => (Matched: 0, h.Score));
        if (query.TermExpressions.Count == 1)
        {
            return ranking.ToDictionary(k => k.Key, k => (1, k.Value.Score));
        }

        foreach (var term in query.TermExpressions)
        {
            var ids = await db.Database
                .SqlQuery<long>($"SELECT rowid AS Value FROM RecipeSearch WHERE RecipeSearch MATCH {term}")
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var id in ids)
            {
                if (ranking.TryGetValue((int)id, out var r))
                {
                    ranking[(int)id] = (r.Matched + 1, r.Score);
                }
            }
        }

        return ranking;
    }

    internal static RecipeCard Card(int id, string slug, string title, string? description, (int? Min, int? Max) total,
        (int? Min, int? Max) prep, (int? Min, int? Max) cook, int? rating, RecipeStatus status, string? imageId = null)
    {
        var time = total.Min is not null ? total : TimeText.Sum(prep.Min, prep.Max, cook.Min, cook.Max);
        var summary = description is null ? null
            : description.Length <= SummaryLength ? description
            : description[..SummaryLength].TrimEnd() + "…";
        return new RecipeCard(id, slug, title, summary, time.Min, time.Max, rating, status) { ImageId = imageId };
    }
}
