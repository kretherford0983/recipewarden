using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Shopping;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Shopping;

/// <summary>
/// ADR 0030, seed shopping-v1 (run once, after the measurement seed): store sections, the starter pantry and common
/// package sizes for the seeded foods. A value already set (by an editor) is kept.
/// </summary>
public sealed class ShoppingSeeder(AppDbContext db, TimeProvider clock)
{
    public const string SeedName = "shopping-v1";

    public async Task<bool> ApplyAsync(CancellationToken cancellationToken = default)
    {
        if (await db.SeedRuns.AnyAsync(r => r.Name == SeedName, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var units = await db.Units.AsNoTracking().ToDictionaryAsync(u => u.Code, u => u.Id, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);
        foreach (var food in await db.Foods.ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            food.StoreSection ??= ShoppingDefaults.SectionFor(food.Name, food.Category);
            if (ShoppingDefaults.IsPantry(food.Name, food.Category))
            {
                food.IsPantryStaple = true;
            }

            if (food.PackageSize is null && ShoppingDefaults.PackageFor(food.Name) is { } package)
            {
                food.PackageSize = package.Size;
                food.PackageUnitId = package.UnitCode is null ? null : units.GetValueOrDefault(package.UnitCode);
                food.PackageName = package.Name;
            }
        }

        db.SeedRuns.Add(new SeedRun { Name = SeedName, AppliedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
