using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Nutrition;
using RecipeWarden.Core.Organization;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Measurement;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Infrastructure.Nutrition;

/// <summary>A recipe's nutrition and allergens as the pages show them.</summary>
public sealed record RecipeNutrition(
    NutritionPanel? Panel,
    NutritionResult Calculated,
    NutrientValues? Manual,
    NutrientValues? Imported,
    IReadOnlyDictionary<AllergenCode, AllergenStatus> Allergens,
    IReadOnlyDictionary<AllergenCode, AllergenStatus> Detected,
    IReadOnlyDictionary<AllergenCode, AllergenStatus> ManualAllergens,
    bool HasUnmatched);

/// <summary>A row of the unmatched-ingredient report (FR-NUT-03).</summary>
public sealed record UnmatchedRow(string Name, int? FoodId, string? FoodName, int Recipes, int Lines, string Reason);

/// <summary>A nutrient row found by search.</summary>
public sealed record NutrientChoice(int Id, string Description, string? Category, string Source, decimal? Calories, bool Removed);

public sealed record NutritionEditResult(bool Ok, string? Error)
{
    public static NutritionEditResult Success { get; } = new(true, null);

    public static NutritionEditResult Fail(string error) => new(false, error);
}

/// <summary>
/// FR-NUT-01 to 04, FR-ALG-01/02, BR-50 to BR-54, ADR 0034 to 0036: calculates nutrition on view, keeps manual and
/// imported panels and manual allergen rows, reports unmatched ingredients, maps them, and adds foods from labels.
/// Local data only; nothing is fetched.
/// </summary>
public sealed class NutritionService(AppDbContext db, FoodCatalogService foods, TimeProvider clock)
{
    public const string NotFound = "That no longer exists.";
    public const string NeedsRemap = "Its USDA food is not in the current dataset; map it again.";
    public const string NoFood = "No food matches this ingredient.";
    public const string NoNutrients = "The food has no nutrition data yet.";
    public const int SearchLimit = 30;

    public async Task<RecipeNutrition?> GetAsync(int recipeId, CancellationToken cancellationToken = default)
    {
        var recipe = await db.Recipes.AsNoTracking().Where(r => r.Id == recipeId)
            .Select(r => new { r.Servings, r.IsPrecision }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (recipe is null)
        {
            return null;
        }

        var ingredients = (await db.RecipeIngredients.AsNoTracking().Include(i => i.Unit).Include(i => i.Group)
            .Where(i => i.RecipeId == recipeId).ToListAsync(cancellationToken).ConfigureAwait(false))
            .OrderBy(i => i.Group?.SortOrder ?? -1).ThenBy(i => i.SortOrder).ToList();
        var foodIds = ingredients.Where(i => i.FoodId != null).Select(i => i.FoodId!.Value).Distinct().ToList();
        var nutritionFoods = await FoodsAsync(foodIds, cancellationToken).ConfigureAwait(false);
        var catalog = await foods.LoadCatalogAsync(cancellationToken).ConfigureAwait(false);
        var context = new ScaleContext(1m, DisplayUnits.AsWritten, recipe.IsPrecision, catalog);

        var inputs = new List<NutritionLineInput>();
        foreach (var ingredient in ingredients)
        {
            var parsed = RecipeService.ToParsed(ingredient);
            NutritionFood? food = ingredient.FoodId is { } fid && nutritionFoods.TryGetValue(fid, out var f) ? f : null;
            decimal? purchase = null;
            if (food is not null && parsed.Quantity is not null && !string.IsNullOrEmpty(parsed.State))
            {
                var scaled = IngredientScaler.Scale(new ScaleInput(parsed, ingredient.FoodId, RecipeScaler.PlusAmount(parsed.Note)), context);
                purchase = scaled.PurchaseGrams is { } low ? (low + (scaled.PurchaseGramsMax ?? low)) / 2 : null;
            }

            inputs.Add(new NutritionLineInput(parsed, food, purchase));
        }

        var calculated = NutritionRules.Calculate(inputs, recipe.Servings);
        var facts = await db.NutritionFacts.AsNoTracking().Where(n => n.RecipeId == recipeId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var manual = facts.FirstOrDefault(n => n.Source == NutritionSource.Manual)?.Values();
        var imported = facts.FirstOrDefault(n => n.Source == NutritionSource.Imported)?.Values();

        var allergenRows = await db.RecipeAllergens.AsNoTracking().Where(a => a.RecipeId == recipeId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var detected = allergenRows.Where(a => a.Origin == AllergenOrigin.Auto).ToDictionary(a => a.Allergen, a => a.Status);
        var manualAllergens = allergenRows.Where(a => a.Origin == AllergenOrigin.Manual).ToDictionary(a => a.Allergen, a => a.Status);
        return new RecipeNutrition(NutritionRules.Choose(manual, imported, calculated), calculated, manual, imported,
            AllergenRules.Effective(detected, manualAllergens), detected, manualAllergens,
            ingredients.Any(i => i.FoodId == null && i.Quantity != null));
    }

    /// <summary>What each food brings: its nutrient row (an editor mapping first, BR-54), density, portions and package.</summary>
    private async Task<Dictionary<int, NutritionFood>> FoodsAsync(IReadOnlyCollection<int> foodIds, CancellationToken cancellationToken)
    {
        var rows = await (from f in db.Foods.AsNoTracking()
                          where foodIds.Contains(f.Id)
                          join u in db.Units on f.PackageUnitId equals u.Id into us
                          from u in us.DefaultIfEmpty()
                          select new { Food = f, Unit = u }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var refIds = rows.Where(r => r.Food.NutrientRefId != null).Select(r => r.Food.NutrientRefId!.Value).ToList();
        var fdcIds = rows.Where(r => r.Food.NutrientRefId == null && r.Food.FdcId != null).Select(r => r.Food.FdcId!.Value).ToList();
        var refs = await db.NutrientRefs.AsNoTracking().Where(n => refIds.Contains(n.Id) || (n.FdcId != null && fdcIds.Contains(n.FdcId.Value)))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var byId = refs.ToDictionary(n => n.Id);
        var byFdc = refs.Where(n => n.FdcId != null).GroupBy(n => n.FdcId!.Value).ToDictionary(g => g.Key, g => g.First());

        var result = new Dictionary<int, NutritionFood>();
        foreach (var row in rows)
        {
            var food = row.Food;
            NutrientRef? nutrient = food.NutrientRefId is { } rid ? byId.GetValueOrDefault(rid)
                : food.FdcId is { } fdc ? byFdc.GetValueOrDefault(fdc) : null;
            if (nutrient is null)
            {
                continue;
            }

            var density = food.DensityGPerMl ?? nutrient.DensityGPerMl;
            decimal? packageGrams = null;
            if (food.PackageSize is { } size && size > 0 && row.Unit is { } unit)
            {
                packageGrams = unit.Kind == UnitKind.Mass ? size * unit.ToBase
                    : unit.Kind == UnitKind.Volume && density is { } d ? size * unit.ToBase * d : null;
            }

            result[food.Id] = new NutritionFood(nutrient.Values(), density, Portions(nutrient.PortionsJson), food.PackageName, packageGrams);
        }

        return result;
    }

    private static Dictionary<string, decimal>? Portions(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, decimal>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>FR-NUT-01, ADR 0035: sets (or, with null, clears) a recipe's manual panel.</summary>
    public Task<NutritionEditResult> SetManualAsync(int recipeId, NutrientValues? values, string userId, CancellationToken cancellationToken = default) =>
        SetAsync(recipeId, NutritionSource.Manual, values, userId, cancellationToken);

    /// <summary>ADR 0035: the panel a page or recipe-md file gave on import.</summary>
    public Task<NutritionEditResult> SetImportedAsync(int recipeId, NutrientValues? values, string userId, CancellationToken cancellationToken = default) =>
        SetAsync(recipeId, NutritionSource.Imported, values, userId, cancellationToken);

    private async Task<NutritionEditResult> SetAsync(int recipeId, NutritionSource source, NutrientValues? values, string userId,
        CancellationToken cancellationToken)
    {
        if (!await db.Recipes.AnyAsync(r => r.Id == recipeId, cancellationToken).ConfigureAwait(false))
        {
            return NutritionEditResult.Fail(NotFound);
        }

        if (values is not null && Bad(values))
        {
            return NutritionEditResult.Fail(NutritionRules.BadValue);
        }

        var row = await db.NutritionFacts.FirstOrDefaultAsync(n => n.RecipeId == recipeId && n.Source == source, cancellationToken).ConfigureAwait(false);
        if (values is null || values.IsEmpty)
        {
            if (row is not null)
            {
                db.NutritionFacts.Remove(row);
            }
        }
        else
        {
            if (row is null)
            {
                row = new NutritionFacts { RecipeId = recipeId, Source = source };
                db.NutritionFacts.Add(row);
            }

            row.SetValues(values);
            row.UpdatedAt = clock.GetUtcNow();
            row.UpdatedById = userId;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return NutritionEditResult.Success;
    }

    private static bool Bad(NutrientValues v) =>
        new[] { v.Calories, v.FatG, v.SatFatG, v.CholesterolMg, v.SodiumMg, v.CarbsG, v.FiberG, v.SugarG, v.ProteinG }.Any(x => x is < 0 or > 100_000);

    /// <summary>FR-ALG-02, BR-52: replaces a recipe's manual allergen rows (none means "use detection").</summary>
    public async Task<NutritionEditResult> SetAllergensAsync(int recipeId, IReadOnlyDictionary<AllergenCode, AllergenStatus> manual,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manual);
        if (!await db.Recipes.AnyAsync(r => r.Id == recipeId, cancellationToken).ConfigureAwait(false))
        {
            return NutritionEditResult.Fail(NotFound);
        }

        await db.RecipeAllergens.Where(a => a.RecipeId == recipeId && a.Origin == AllergenOrigin.Manual)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        foreach (var entry in db.ChangeTracker.Entries<RecipeAllergen>().Where(e => e.Entity.RecipeId == recipeId
                     && e.Entity.Origin == AllergenOrigin.Manual).ToList())
        {
            entry.State = EntityState.Detached;
        }

        var now = clock.GetUtcNow();
        foreach (var (code, status) in manual.Where(m => Enum.IsDefined(m.Key) && Enum.IsDefined(m.Value)))
        {
            db.RecipeAllergens.Add(new RecipeAllergen { RecipeId = recipeId, Allergen = code, Origin = AllergenOrigin.Manual, Status = status, UpdatedAt = now });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return NutritionEditResult.Success;
    }

    /// <summary>
    /// FR-NUT-03, AC-57: ingredients with no nutrients — no food, a food without data, or a food whose USDA row left the
    /// dataset — by how many recipes use them. Lines without an amount are left out (they are not counted).
    /// </summary>
    public async Task<IReadOnlyList<UnmatchedRow>> UnmatchedAsync(CancellationToken cancellationToken = default)
    {
        var lines = await (from i in db.RecipeIngredients.AsNoTracking()
                           join r in db.Recipes on i.RecipeId equals r.Id
                           where i.Quantity != null
                           join f in db.Foods on i.FoodId equals f.Id into fs
                           from f in fs.DefaultIfEmpty()
                           select new
                           {
                               i.RecipeId, i.Name, FoodId = (int?)f.Id, FoodName = (string?)f.Name,
                               RefId = f == null ? null : f.NutrientRefId, Fdc = f == null ? null : f.FdcId,
                           }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var refIds = lines.Where(l => l.RefId != null).Select(l => l.RefId!.Value).Distinct().ToList();
        var fdcIds = lines.Where(l => l.RefId == null && l.Fdc != null).Select(l => l.Fdc!.Value).Distinct().ToList();
        var refs = await db.NutrientRefs.AsNoTracking().Where(n => refIds.Contains(n.Id) || (n.FdcId != null && fdcIds.Contains(n.FdcId.Value)))
            .Select(n => new { n.Id, n.FdcId, n.RemovedInVersion }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var removedIds = refs.Where(r => r.RemovedInVersion != null).Select(r => r.Id).ToHashSet();
        var knownIds = refs.Select(r => r.Id).ToHashSet();
        var removedFdc = refs.Where(r => r.RemovedInVersion != null && r.FdcId != null).Select(r => r.FdcId!.Value).ToHashSet();
        var knownFdc = refs.Where(r => r.FdcId != null).Select(r => r.FdcId!.Value).ToHashSet();

        string? Reason(int? foodId, int? refId, int? fdc) =>
            foodId is null ? NoFood
            : refId is { } r ? (removedIds.Contains(r) ? NeedsRemap : knownIds.Contains(r) ? null : NoNutrients)
            : fdc is { } d ? (removedFdc.Contains(d) ? NeedsRemap : knownFdc.Contains(d) ? null : NoNutrients)
            : NoNutrients;

        return lines.Select(l => new { l, Reason = Reason(l.FoodId, l.RefId, l.Fdc) })
            .Where(x => x.Reason is not null)
            .GroupBy(x => x.l.FoodId is { } id ? "f:" + id : "n:" + NameRules.Normalize(x.l.Name))
            .Select(g => new UnmatchedRow(g.First().l.FoodName ?? g.First().l.Name.Trim(), g.First().l.FoodId, g.First().l.FoodName,
                g.Select(x => x.l.RecipeId).Distinct().Count(), g.Count(), g.First().Reason!))
            .Where(r => r.Name.Length > 0)
            .OrderByDescending(r => r.Recipes).ThenByDescending(r => r.Lines).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>FR-NUT-03: nutrient rows whose description has every word, custom foods first, current rows before removed ones.</summary>
    public async Task<IReadOnlyList<NutrientChoice>> SearchAsync(string? query, CancellationToken cancellationToken = default)
    {
        var words = NameRules.Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 1).Take(6).ToList();
        if (words.Count == 0)
        {
            return [];
        }

        var q = db.NutrientRefs.AsNoTracking();
        foreach (var word in words)
        {
            q = q.Where(n => n.NormalizedDescription.Contains(word));
        }

        var found = await q.Select(n => new { n.Id, n.Description, n.Category, n.Source, n.Calories, n.RemovedInVersion })
            .Take(500).ToListAsync(cancellationToken).ConfigureAwait(false);
        return found
            .OrderBy(n => n.RemovedInVersion is null ? 0 : 1)
            .ThenBy(n => n.Source == NutrientSources.Custom ? 0 : 1)
            .ThenBy(n => n.Description.Length)
            .ThenBy(n => n.Description, StringComparer.OrdinalIgnoreCase)
            .Take(SearchLimit)
            .Select(n => new NutrientChoice(n.Id, n.Description, n.Category, n.Source, n.Calories, n.RemovedInVersion is not null))
            .ToList();
    }

    /// <summary>
    /// FR-NUT-03, BR-54, AC-57, ADR 0034: maps a food (or an ingredient name with no food, by adding a household food)
    /// to a nutrient row. Every recipe using it is counted from now on; allergens are re-detected for them.
    /// </summary>
    public async Task<NutritionEditResult> MapAsync(int? foodId, string? name, int nutrientRefId, string userId, CancellationToken cancellationToken = default)
    {
        var nutrient = await db.NutrientRefs.AsNoTracking().FirstOrDefaultAsync(n => n.Id == nutrientRefId, cancellationToken).ConfigureAwait(false);
        if (nutrient is null)
        {
            return NutritionEditResult.Fail(NotFound);
        }

        if (foodId is { } id)
        {
            var food = await db.Foods.FirstOrDefaultAsync(f => f.Id == id, cancellationToken).ConfigureAwait(false);
            if (food is null)
            {
                return NutritionEditResult.Fail(NotFound);
            }

            food.NutrientRefId = nutrient.Id;
            food.DensityGPerMl ??= nutrient.DensityGPerMl;
            food.UpdatedAt = clock.GetUtcNow();
            food.UpdatedById = userId;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return NutritionEditResult.Success;
        }

        var added = await foods.AddFoodAsync(name, nutrient.DensityGPerMl, null, userId, cancellationToken).ConfigureAwait(false);
        if (!added.Ok)
        {
            return NutritionEditResult.Fail(added.Error!);
        }

        var normalized = FoodMatcher.Normalize(name!.Trim());
        var created = await db.Foods.FirstAsync(f => f.NormalizedName == normalized, cancellationToken).ConfigureAwait(false);
        created.NutrientRefId = nutrient.Id;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return NutritionEditResult.Success;
    }

    /// <summary>FR-NUT-04, AC-57: a custom food from a package label, stored per 100 g.</summary>
    public async Task<(NutritionEditResult Result, int? Id)> AddLabelFoodAsync(string? name, decimal servingSize, bool servingInMl, decimal? density,
        NutrientValues label, string userId, CancellationToken cancellationToken = default)
    {
        if (FoodRules.ValidateName(name) is { } nameError)
        {
            return (NutritionEditResult.Fail(nameError), null);
        }

        var (per100, error) = NutritionRules.FromLabel(servingSize, servingInMl, density, label);
        if (error is not null)
        {
            return (NutritionEditResult.Fail(error), null);
        }

        var now = clock.GetUtcNow();
        var description = name!.Trim() + " (label)";
        var row = new NutrientRef
        {
            Source = NutrientSources.Custom,
            Description = description,
            NormalizedDescription = NameRules.Normalize(description),
            Category = "Custom food",
            DensityGPerMl = servingInMl ? density ?? 1m : density,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedById = userId,
        };
        row.SetValues(per100!);
        db.NutrientRefs.Add(row);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (NutritionEditResult.Success, row.Id);
    }

    /// <summary>FR-NUT-05, AC-58: the current dataset (the newest applied).</summary>
    public async Task<NutrientDataset?> DatasetAsync(CancellationToken cancellationToken = default) =>
        (await db.NutrientDatasets.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false))
            .OrderByDescending(d => d.AppliedAt).ThenByDescending(d => d.Id).FirstOrDefault();
}
