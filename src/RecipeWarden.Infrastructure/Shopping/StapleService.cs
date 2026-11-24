using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Import;
using RecipeWarden.Core.Organization;
using RecipeWarden.Core.Shopping;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Measurement;

namespace RecipeWarden.Infrastructure.Shopping;

/// <summary>
/// FR-SHOP-05, ADR 0056: the household staples list (one list, any Editor can change it; every change is audited by
/// the save interceptor, AC-74). A catalogue food is a staple when Food.IsPantryStaple is set (FR-SHOP-03, also set
/// from /manage/conversions); its <see cref="Staple"/> row holds where it lives and the usual amount, and is made on
/// demand. Rows without a food are non-food staples.
/// </summary>
public sealed class StapleService(AppDbContext db, FoodCatalogService foods, TimeProvider clock)
{
    /// <summary>Every staple: flagged foods (with their details when saved) and non-food staples.</summary>
    public async Task<IReadOnlyList<StapleInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.Staples.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var byFood = rows.Where(r => r.FoodId is not null).ToDictionary(r => r.FoodId!.Value);
        var flagged = await db.Foods.AsNoTracking().Where(f => f.IsPantryStaple).Select(f => new { f.Id, f.Name })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var list = flagged.Select(f => byFood.TryGetValue(f.Id, out var r)
                ? new StapleInfo(r.Id, f.Id, f.Name, r.Location, r.Restock, r.Section, r.LastRestockedAt)
                : new StapleInfo(0, f.Id, f.Name, StapleRules.DefaultLocation, null, null, null))
            .Concat(rows.Where(r => r.FoodId is null).Select(r => new StapleInfo(r.Id, null, r.Name, r.Location, r.Restock, r.Section, r.LastRestockedAt)))
            .ToList();
        return StapleRules.ByLocation(list).SelectMany(g => g).ToList();
    }

    /// <summary>Adds a staple: a name the catalogue knows becomes a food staple, anything else a non-food staple.</summary>
    public async Task<string?> AddAsync(string? name, string? location, string? restock, string? section, CancellationToken cancellationToken = default)
    {
        var clean = ImportText.Line(name);
        section = string.IsNullOrWhiteSpace(section) ? null : section;
        if (StapleRules.Validate(clean, location, restock, section) is { } error)
        {
            return error;
        }

        if (await db.Staples.CountAsync(cancellationToken).ConfigureAwait(false) >= StapleRules.MaxStaples)
        {
            return StapleRules.TooMany;
        }

        var matcher = await foods.LoadMatcherAsync(cancellationToken).ConfigureAwait(false);
        var foodId = matcher.Match(clean);
        var normalized = NameRules.Normalize(clean!);
        Food? food = null;
        if (foodId is { } id)
        {
            food = await db.Foods.SingleAsync(f => f.Id == id, cancellationToken).ConfigureAwait(false);
            if (food.IsPantryStaple && await db.Staples.AnyAsync(s => s.FoodId == id, cancellationToken).ConfigureAwait(false))
            {
                return StapleRules.AlreadyAStaple;
            }

            normalized = NameRules.Normalize(food.Name);
        }

        var sameName = await db.Staples.AsNoTracking().FirstOrDefaultAsync(s => s.NormalizedName == normalized, cancellationToken).ConfigureAwait(false);
        if (sameName is not null && (foodId is null || sameName.FoodId != foodId))
        {
            return StapleRules.AlreadyAStaple;
        }

        var row = foodId is null ? null : await db.Staples.FirstOrDefaultAsync(s => s.FoodId == foodId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            row = new Staple { FoodId = foodId, CreatedAt = clock.GetUtcNow() };
            db.Staples.Add(row);
        }

        row.Name = food?.Name ?? clean!;
        row.NormalizedName = normalized;
        row.Location = location!;
        row.Restock = string.IsNullOrWhiteSpace(restock) ? null : restock.Trim();
        row.Section = food is null ? section : section ?? food.StoreSection;
        if (food is not null)
        {
            food.IsPantryStaple = true;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return null;
    }

    /// <summary>Changes where a staple lives, its usual amount and section. A food staple's row is made on first edit.</summary>
    public async Task<string?> UpdateAsync(int id, int? foodId, string? location, string? restock, string? section, CancellationToken cancellationToken = default)
    {
        section = string.IsNullOrWhiteSpace(section) ? null : section;
        var row = id > 0
            ? await db.Staples.FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false)
            : foodId is { } fid ? await db.Staples.FirstOrDefaultAsync(s => s.FoodId == fid, cancellationToken).ConfigureAwait(false) : null;
        var food = (row?.FoodId ?? foodId) is { } f ? await db.Foods.FirstOrDefaultAsync(x => x.Id == f && x.IsPantryStaple, cancellationToken).ConfigureAwait(false) : null;
        if (row is null && food is null)
        {
            return StapleRules.NameRequired;
        }

        if (StapleRules.Validate(row?.Name ?? food!.Name, location, restock, section) is { } error)
        {
            return error;
        }

        if (row is null)
        {
            row = new Staple { FoodId = food!.Id, Name = food.Name, NormalizedName = NameRules.Normalize(food.Name), CreatedAt = clock.GetUtcNow() };
            db.Staples.Add(row);
        }

        row.Location = location!;
        row.Restock = string.IsNullOrWhiteSpace(restock) ? null : restock.Trim();
        row.Section = section;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return null;
    }

    /// <summary>Removes a staple; a food stops being a staple (its flag is cleared too).</summary>
    public async Task<bool> RemoveAsync(int id, int? foodId, CancellationToken cancellationToken = default)
    {
        var row = id > 0 ? await db.Staples.FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false) : null;
        var fid = row?.FoodId ?? foodId;
        var food = fid is { } f ? await db.Foods.FirstOrDefaultAsync(x => x.Id == f, cancellationToken).ConfigureAwait(false) : null;
        if (row is null && food is null)
        {
            return false;
        }

        if (row is not null)
        {
            db.Staples.Remove(row);
        }
        else if (await db.Staples.FirstOrDefaultAsync(s => s.FoodId == fid, cancellationToken).ConfigureAwait(false) is { } detail)
        {
            db.Staples.Remove(detail);
        }

        if (food is not null)
        {
            food.IsPantryStaple = false;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
