using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Shopping;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Measurement;

/// <summary>A food row on /manage/conversions.</summary>
public sealed record FoodRow(int Id, string Name, string? Category, decimal? DensityGPerMl, string Source, string? SourceNote,
    IReadOnlyList<string> Aliases, int RecipeLines)
{
    /// <summary>ADR 0018.</summary>
    public FoodForm Form { get; init; }

    /// <summary>ADR 0030: store section, pantry flag and package (size, unit code or null for a count, name).</summary>
    public string? StoreSection { get; init; }
    public bool IsPantryStaple { get; init; }
    public decimal? PackageSize { get; init; }
    public string? PackageUnit { get; init; }
    public string? PackageName { get; init; }
}

/// <summary>A vague unit (pinch, dash …) and its current value in teaspoons.</summary>
public sealed record VagueRow(int Id, string Code, string Name, decimal Teaspoons, decimal DefaultTeaspoons);

public sealed record YieldRow(int Id, int FoodId, string Food, string FromState, string ToState, decimal Ratio, decimal RatioMax,
    decimal? PreparedDensityGPerMl, string Basis, string Source);

/// <summary>Result of an edit on /manage/conversions: ok, or the message to show next to the field.</summary>
public sealed record ConversionEditResult(bool Ok, string? Error)
{
    public static ConversionEditResult Success { get; } = new(true, null);

    public static ConversionEditResult Fail(string error) => new(false, error);
}

/// <summary>
/// FR-MEAS-01/04/05, ADR 0013: the measurement knowledge base. Loads the catalog the Core engine works from, links
/// ingredients to foods, and holds the editor operations behind /manage/conversions.
/// </summary>
public sealed class FoodCatalogService(AppDbContext db, TimeProvider clock)
{
    public const string NotFound = "That entry no longer exists";
    public const string DuplicateName = "Another food already uses that name or alias";

    /// <summary>Everything the engine needs: units with their current values, foods and yield conversions.</summary>
    public async Task<MeasurementCatalog> LoadCatalogAsync(CancellationToken cancellationToken = default)
    {
        var units = await db.Units.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var foods = await db.Foods.AsNoTracking()
            .Select(f => new FoodFacts(f.Id, f.Name, f.DensityGPerMl, f.Form))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var yields = await db.YieldConversions.AsNoTracking()
            .Select(y => new YieldFacts(y.FoodId, y.FromState, y.ToState, y.Ratio, y.RatioMax, y.PreparedDensityGPerMl, y.Basis))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var unitDefs = units.Select(u => UnitCatalog.FindByCode(u.Code) is { } d
            ? d with { ToBase = u.ToBase }
            : new UnitDefinition(u.Code, u.Name, u.PluralName, u.Kind, u.System, u.ToBase, u.Aliases.Split('|')));
        return new MeasurementCatalog(unitDefs, foods, yields);
    }

    public async Task<FoodMatcher> LoadMatcherAsync(CancellationToken cancellationToken = default)
    {
        var foods = await db.Foods.AsNoTracking().Include(f => f.Aliases).OrderBy(f => f.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new FoodMatcher(foods.Select(f => new FoodNames(f.Id, f.Name, f.PluralName, f.Aliases.Select(a => a.Alias).ToList())));
    }

    /// <summary>
    /// ADR 0013: links ingredients that have no food yet. Safe to run at every start: lines already linked are left
    /// alone, so an editor's mapping is never changed, and a run with nothing new to link writes nothing.
    /// </summary>
    public async Task<int> LinkUnmatchedAsync(CancellationToken cancellationToken = default)
    {
        var matcher = await LoadMatcherAsync(cancellationToken).ConfigureAwait(false);
        var names = await db.RecipeIngredients.IgnoreQueryFilters().Where(i => i.FoodId == null)
            .Select(i => i.Name).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        var linked = 0;
        foreach (var name in names)
        {
            if (matcher.Match(name) is { } foodId)
            {
                linked += await db.RecipeIngredients.IgnoreQueryFilters()
                    .Where(i => i.FoodId == null && i.Name == name)
                    .ExecuteUpdateAsync(u => u.SetProperty(i => i.FoodId, foodId), cancellationToken).ConfigureAwait(false);
            }
        }

        return linked;
    }

    public async Task<IReadOnlyList<FoodRow>> ListFoodsAsync(string? filter, CancellationToken cancellationToken = default)
    {
        var foods = await db.Foods.AsNoTracking().Include(f => f.Aliases)
            .Select(f => new
            {
                f.Id, f.Name, f.NormalizedName, f.Category, f.DensityGPerMl, f.Source, f.SourceNote, f.Form,
                f.StoreSection, f.IsPantryStaple, f.PackageSize, f.PackageName,
                PackageUnit = db.Units.Where(u => u.Id == f.PackageUnitId).Select(u => u.Code).FirstOrDefault(),
                Aliases = f.Aliases.Select(a => a.Alias).ToList(),
                AliasKeys = f.Aliases.Select(a => a.NormalizedAlias).ToList(),
                Lines = db.RecipeIngredients.Count(i => i.FoodId == f.Id),
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var key = FoodMatcher.Normalize(filter);
        return foods
            .Where(f => key.Length == 0 || f.NormalizedName.Contains(key, StringComparison.Ordinal) || f.AliasKeys.Any(a => a.Contains(key, StringComparison.Ordinal)))
            .OrderBy(f => f.Category).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Select(f => new FoodRow(f.Id, f.Name, f.Category, f.DensityGPerMl, f.Source, f.SourceNote,
                f.Aliases.Order(StringComparer.OrdinalIgnoreCase).ToList(), f.Lines)
            {
                Form = f.Form,
                StoreSection = f.StoreSection,
                IsPantryStaple = f.IsPantryStaple,
                PackageSize = f.PackageSize,
                PackageUnit = f.PackageUnit,
                PackageName = f.PackageName,
            })
            .ToList();
    }

    public async Task<IReadOnlyList<VagueRow>> ListVagueAsync(CancellationToken cancellationToken = default)
    {
        var tsp = UnitCatalog.FindByCode("tsp")!.ToBase;
        var units = await db.Units.AsNoTracking().Where(u => u.IsVague).OrderBy(u => u.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return units
            .Where(u => UnitCatalog.FindByCode(u.Code) is { ToBase: > 0m })
            .Select(u => new VagueRow(u.Id, u.Code, u.Name, Math.Round(u.ToBase / tsp, 4), Math.Round(UnitCatalog.FindByCode(u.Code)!.ToBase / tsp, 4)))
            .ToList();
    }

    public async Task<IReadOnlyList<YieldRow>> ListYieldsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.YieldConversions.AsNoTracking().Include(y => y.Food)
            .Select(y => new YieldRow(y.Id, y.FoodId, y.Food!.Name, y.FromState, y.ToState, y.Ratio, y.RatioMax, y.PreparedDensityGPerMl, y.Basis, y.Source))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.OrderBy(r => r.Food, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.FromState, StringComparer.Ordinal).ToList();
    }

    /// <summary>Adds a household food (Custom), then links any unlinked ingredient lines it matches.</summary>
    public async Task<ConversionEditResult> AddFoodAsync(string? name, decimal? density, string? aliases, string userId,
        CancellationToken cancellationToken = default)
    {
        if ((FoodRules.ValidateName(name) ?? FoodRules.ValidateDensity(density)) is { } error)
        {
            return ConversionEditResult.Fail(error);
        }

        var clean = name!.Trim();
        var aliasList = FoodRules.ParseAliases(aliases, clean);
        if (await NameTakenAsync(null, [clean, .. aliasList], cancellationToken).ConfigureAwait(false))
        {
            return ConversionEditResult.Fail(DuplicateName);
        }

        var now = clock.GetUtcNow();
        var food = new Food
        {
            Name = clean,
            NormalizedName = FoodMatcher.Normalize(clean),
            PluralName = clean,
            DensityGPerMl = density,
            Form = FoodRules.DefaultForm(clean, null),
            Source = FoodSources.Custom,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedById = userId,
        };
        food.Aliases.AddRange(aliasList.Select(a => new FoodAlias { Alias = a, NormalizedAlias = FoodMatcher.Normalize(a) }));
        db.Foods.Add(food);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await LinkUnmatchedAsync(cancellationToken).ConfigureAwait(false);
        return ConversionEditResult.Success;
    }

    /// <summary>FR-MEAS-05: changes a food's density and aliases. The name stays: it is what lines were matched on.</summary>
    public Task<ConversionEditResult> UpdateFoodAsync(int id, decimal? density, string? aliases, string userId,
        CancellationToken cancellationToken = default) =>
        UpdateFoodAsync(id, density, aliases, null, userId, cancellationToken);

    /// <summary>FR-MEAS-05, ADR 0018: as above, and the food's form when given.</summary>
    public async Task<ConversionEditResult> UpdateFoodAsync(int id, decimal? density, string? aliases, FoodForm? form, string userId,
        CancellationToken cancellationToken = default)
    {
        if (FoodRules.ValidateDensity(density) is { } error)
        {
            return ConversionEditResult.Fail(error);
        }

        var food = await db.Foods.Include(f => f.Aliases).FirstOrDefaultAsync(f => f.Id == id, cancellationToken).ConfigureAwait(false);
        if (food is null)
        {
            return ConversionEditResult.Fail(NotFound);
        }

        var aliasList = FoodRules.ParseAliases(aliases, food.Name);
        if (await NameTakenAsync(id, aliasList, cancellationToken).ConfigureAwait(false))
        {
            return ConversionEditResult.Fail(DuplicateName);
        }

        food.DensityGPerMl = density;
        if (form is { } f)
        {
            food.Form = f;
        }

        db.FoodAliases.RemoveRange(food.Aliases.Where(a => aliasList.All(n => FoodMatcher.Normalize(n) != a.NormalizedAlias)));
        foreach (var alias in aliasList.Where(n => food.Aliases.All(a => a.NormalizedAlias != FoodMatcher.Normalize(n))))
        {
            food.Aliases.Add(new FoodAlias { Alias = alias, NormalizedAlias = FoodMatcher.Normalize(alias) });
        }

        food.UpdatedAt = clock.GetUtcNow();
        food.UpdatedById = userId;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await LinkUnmatchedAsync(cancellationToken).ConfigureAwait(false);
        return ConversionEditResult.Success;
    }

    /// <summary>ADR 0030, FR-SHOP-03, BR-41: a food's store section, pantry flag and package size.</summary>
    public async Task<ConversionEditResult> UpdateShoppingAsync(int id, string? section, bool pantry, decimal? packageSize, string? packageUnit,
        string? packageName, string userId, CancellationToken cancellationToken = default)
    {
        section = string.IsNullOrWhiteSpace(section) ? null : section.Trim();
        packageUnit = string.IsNullOrWhiteSpace(packageUnit) ? null : packageUnit.Trim();
        if (ShoppingRules.ValidateFood(section, packageSize, packageUnit, packageName) is { } error)
        {
            return ConversionEditResult.Fail(error);
        }

        var food = await db.Foods.FirstOrDefaultAsync(f => f.Id == id, cancellationToken).ConfigureAwait(false);
        if (food is null)
        {
            return ConversionEditResult.Fail(NotFound);
        }

        int? unitId = null;
        if (packageUnit is not null)
        {
            unitId = await db.Units.Where(u => u.Code == packageUnit).Select(u => (int?)u.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }

        var hasPackage = packageSize is not null;
        food.StoreSection = section;
        food.IsPantryStaple = pantry;
        food.PackageSize = hasPackage ? packageSize : null;
        food.PackageUnitId = hasPackage ? unitId : null;
        food.PackageName = hasPackage ? packageName!.Trim() : null;
        food.UpdatedAt = clock.GetUtcNow();
        food.UpdatedById = userId;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ConversionEditResult.Success;
    }

    /// <summary>Deletes a food; linked ingredient lines keep their text and lose the link (FoodId set to null).</summary>
    public async Task<bool> DeleteFoodAsync(int id, CancellationToken cancellationToken = default) =>
        await db.Foods.Where(f => f.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) > 0;

    /// <summary>FR-MEAS-05: a vague unit's value, entered in teaspoons.</summary>
    public async Task<ConversionEditResult> UpdateVagueAsync(int unitId, decimal teaspoons, CancellationToken cancellationToken = default)
    {
        if (FoodRules.ValidateVagueTsp(teaspoons) is { } error)
        {
            return ConversionEditResult.Fail(error);
        }

        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == unitId && u.IsVague, cancellationToken).ConfigureAwait(false);
        if (unit is null || UnitCatalog.FindByCode(unit.Code) is not { ToBase: > 0m })
        {
            return ConversionEditResult.Fail(NotFound); // handfuls have no measure to edit
        }

        unit.ToBase = teaspoons * UnitCatalog.FindByCode("tsp")!.ToBase;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ConversionEditResult.Success;
    }

    public async Task<ConversionEditResult> UpdateYieldAsync(int id, decimal ratio, decimal ratioMax, decimal? preparedDensity, string? basis,
        string userId, CancellationToken cancellationToken = default)
    {
        if (FoodRules.ValidateYield(ratio, ratioMax, preparedDensity, basis) is { } error)
        {
            return ConversionEditResult.Fail(error);
        }

        var row = await db.YieldConversions.FirstOrDefaultAsync(y => y.Id == id, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return ConversionEditResult.Fail(NotFound);
        }

        row.Ratio = ratio;
        row.RatioMax = ratioMax;
        row.PreparedDensityGPerMl = preparedDensity;
        row.FromUnitKind = preparedDensity is null ? UnitKind.Mass : UnitKind.Volume;
        row.Basis = basis!.Trim();
        row.UpdatedAt = clock.GetUtcNow();
        row.UpdatedById = userId;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ConversionEditResult.Success;
    }

    private async Task<bool> NameTakenAsync(int? exceptFoodId, IEnumerable<string> names, CancellationToken cancellationToken)
    {
        var keys = names.Select(FoodMatcher.Normalize).Where(k => k.Length > 0).ToList();
        return await db.Foods.AnyAsync(f => f.Id != exceptFoodId && keys.Contains(f.NormalizedName), cancellationToken).ConfigureAwait(false)
            || await db.FoodAliases.AnyAsync(a => a.FoodId != exceptFoodId && keys.Contains(a.NormalizedAlias), cancellationToken).ConfigureAwait(false);
    }
}
