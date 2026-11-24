using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Nutrition;
using RecipeWarden.Core.Organization;
using RecipeWarden.Core.Python;
using RecipeWarden.Infrastructure.Audit;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Python;

namespace RecipeWarden.Infrastructure.Nutrition;

/// <summary>A suggestion to show next to an unmatched ingredient: a local food, its dataset and the matcher's confidence.</summary>
public sealed record SuggestedMatch(string Name, int NutrientRefId, string Description, string? Dataset, decimal Confidence);

/// <summary>What "Suggest matches" did: names asked, suggestions found, or why nothing was asked (BR-119).</summary>
public sealed record SuggestRun(int Asked, int Found, WorkerFailure Failure);

/// <summary>
/// FR-NUT-07, BR-116, AC-101, AC-102: suggestions for the unmatched-ingredient report from the bundled ingredient
/// matcher, resolved against the local dataset only and cached per name, engine and dataset. Nothing is mapped here:
/// an Editor confirms a suggestion through the ordinary mapping (FR-NUT-03).
/// </summary>
public sealed class FoodSuggestionService(AppDbContext db, NutritionService nutrition, IFoodSuggestionSource source, PythonWorkerHealth health,
    TimeProvider clock)
{
    /// <summary>The current, not dismissed suggestions for these names, by normalized name.</summary>
    public async Task<IReadOnlyDictionary<string, SuggestedMatch>> ForAsync(IEnumerable<string> names, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(names);
        var keys = names.Select(n => NameRules.Normalize(n)).Where(k => k.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var dataset = await DatasetAsync(cancellationToken).ConfigureAwait(false);
        if (keys.Count == 0)
        {
            return new Dictionary<string, SuggestedMatch>(StringComparer.Ordinal);
        }

        var rows = await (from s in db.FoodSuggestions.AsNoTracking()
                          where s.DatasetVersion == dataset && keys.Contains(s.NormalizedName)
                          join n in db.NutrientRefs.AsNoTracking() on s.NutrientRefId equals n.Id into ns
                          from n in ns.DefaultIfEmpty()
                          select new { s.Id, s.NormalizedName, s.DismissedAt, s.Confidence, s.NutrientRefId, n.Description, n.DataType, n.RemovedInVersion })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // The newest row per name decides (a new engine may suggest again after a dismissal, BR-116).
        return rows.GroupBy(r => r.NormalizedName, StringComparer.Ordinal)
            .Select(g => g.MaxBy(r => r.Id)!)
            .Where(r => r.DismissedAt is null && r.NutrientRefId is not null && r.Description is not null && r.RemovedInVersion is null)
            .ToDictionary(r => r.NormalizedName,
                r => new SuggestedMatch(r.NormalizedName, r.NutrientRefId!.Value, r.Description!, NutritionRules.DatasetLabel(r.DataType), r.Confidence ?? 0m),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// Asks the matcher about the names that have no suggestion yet for this dataset and engine (at most
    /// <see cref="FoodSuggestionRules.MaxNamesPerRequest"/>), keeps only candidates that resolve to a local, current food
    /// (BR-116), and remembers the answer, including "none".
    /// </summary>
    public async Task<SuggestRun> SuggestAsync(IEnumerable<string> names, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(names);
        var dataset = await DatasetAsync(cancellationToken).ConfigureAwait(false);
        var engine = CurrentEngine();
        var wanted = names.Select(n => (Name: n.Trim(), Key: NameRules.Normalize(n)))
            .Where(n => n.Key.Length is > 0 and <= 200).DistinctBy(n => n.Key, StringComparer.Ordinal).ToList();
        var keys = wanted.Select(n => n.Key).ToList();
        var cached = await db.FoodSuggestions.AsNoTracking()
            .Where(s => s.DatasetVersion == dataset && keys.Contains(s.NormalizedName) && (engine == null || s.EngineVersion == engine))
            .Select(s => s.NormalizedName).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        var ask = wanted.Where(n => !cached.Contains(n.Key, StringComparer.Ordinal)).Take(FoodSuggestionRules.MaxNamesPerRequest).ToList();
        if (ask.Count == 0)
        {
            return new SuggestRun(0, 0, WorkerFailure.None);
        }

        var outcome = await source.SuggestAsync([.. ask.Select(a => a.Key)], cancellationToken).ConfigureAwait(false);
        if (!outcome.Ok)
        {
            return new SuggestRun(ask.Count, 0, outcome.Failure);
        }

        // SEC-93: an FDC id is only kept when it is a local, current USDA food.
        var fdcIds = outcome.Value!.SelectMany(c => c.Foods).Select(f => (int?)f.FdcId).Distinct().ToList();
        var local = await db.NutrientRefs.AsNoTracking()
            .Where(n => n.Source == NutrientSources.Usda && n.RemovedInVersion == null && fdcIds.Contains(n.FdcId))
            .Select(n => new { n.Id, Fdc = n.FdcId!.Value, n.Description, n.DataType })
            .ToDictionaryAsync(n => n.Fdc, cancellationToken).ConfigureAwait(false);
        var existing = await db.FoodSuggestions
            .Where(s => s.DatasetVersion == dataset && s.EngineVersion == outcome.Engine && keys.Contains(s.NormalizedName))
            .Select(s => s.NormalizedName).ToListAsync(cancellationToken).ConfigureAwait(false);

        var now = clock.GetUtcNow();
        var found = 0;
        foreach (var answer in outcome.Value!)
        {
            if (existing.Contains(answer.Name, StringComparer.Ordinal))
            {
                continue; // already answered (and maybe dismissed) by this engine
            }

            var pick = FoodSuggestionRules.Pick(answer.Foods, local.ContainsKey);
            var food = pick is null ? null : local[pick.FdcId];
            found += food is null ? 0 : 1;
            db.FoodSuggestions.Add(new FoodSuggestion
            {
                NormalizedName = answer.Name, NutrientRefId = food?.Id, FdcId = food?.Fdc, Description = food?.Description,
                DataType = food?.DataType, Confidence = pick?.Confidence, EngineVersion = outcome.Engine!, DatasetVersion = dataset,
                CreatedAt = now,
            });
        }

        using (AuditScope.Suppress()) // a cache, not a person's change
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new SuggestRun(ask.Count, found, WorkerFailure.None);
    }

    /// <summary>BR-116: an Editor's "not this one"; the name gets no suggestion again from this engine.</summary>
    public async Task<bool> DismissAsync(string? name, string userId, CancellationToken cancellationToken = default)
    {
        var key = NameRules.Normalize(name);
        var dataset = await DatasetAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.FoodSuggestions.Where(s => s.NormalizedName == key && s.DatasetVersion == dataset)
            .OrderByDescending(s => s.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (row is null || row.DismissedAt is not null)
        {
            return false;
        }

        row.DismissedAt = clock.GetUtcNow();
        row.DismissedById = userId;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<string> DatasetAsync(CancellationToken cancellationToken) =>
        (await nutrition.DatasetSummaryAsync(cancellationToken).ConfigureAwait(false))?.Key ?? string.Empty;

    /// <summary>The matcher's version from the last self-test ("ingredient-parser-nlp 2.10.0"), or null before one ran.</summary>
    private string? CurrentEngine() =>
        health.Snapshot.Engine?.Split(", ").FirstOrDefault(e => e.StartsWith(PythonFoodSuggestionSource.Library + " ", StringComparison.Ordinal));
}
