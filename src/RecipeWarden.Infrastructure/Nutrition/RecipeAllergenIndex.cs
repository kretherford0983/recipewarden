using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Nutrition;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Nutrition;

/// <summary>
/// ADR 0036, BR-52: rewrites a recipe's detected (Auto) allergen rows from its lines and their foods. Manual rows are
/// never touched. Called when a recipe is saved, when foods are linked or mapped, and once by the backfill.
/// </summary>
public static class RecipeAllergenIndex
{
    public const string BackfillRun = "allergens-v1";

    public static async Task UpdateAsync(AppDbContext db, int recipeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        await UpdateManyAsync(db, [recipeId], cancellationToken).ConfigureAwait(false);
    }

    public static async Task UpdateManyAsync(AppDbContext db, IReadOnlyCollection<int> recipeIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(recipeIds);
        foreach (var chunk in recipeIds.Distinct().Chunk(500))
        {
            var lines = await (from i in db.RecipeIngredients.AsNoTracking()
                               where chunk.Contains(i.RecipeId)
                               join f in db.Foods on i.FoodId equals f.Id into fs
                               from f in fs.DefaultIfEmpty()
                               select new { i.RecipeId, i.RawText, i.Name, Food = f == null ? null : (string?)f.Name })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            await db.RecipeAllergens.Where(a => chunk.Contains(a.RecipeId) && a.Origin == AllergenOrigin.Auto)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

            // The bulk delete bypasses the change tracker; forget rows this context still tracks.
            foreach (var entry in db.ChangeTracker.Entries<RecipeAllergen>().Where(e => chunk.Contains(e.Entity.RecipeId)
                         && e.Entity.Origin == AllergenOrigin.Auto).ToList())
            {
                entry.State = EntityState.Detached;
            }
            var now = DateTimeOffset.UtcNow;
            foreach (var group in lines.GroupBy(l => l.RecipeId))
            {
                foreach (var (code, status) in AllergenRules.ForRecipe(group.Select(l => (Line: l.RawText + " " + l.Name, Food: (string?)l.Food))))
                {
                    db.RecipeAllergens.Add(new RecipeAllergen
                    {
                        RecipeId = group.Key, Allergen = code, Origin = AllergenOrigin.Auto, Status = status, UpdatedAt = now,
                    });
                }
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The once-only pass over existing recipes (and again for a new rules version).</summary>
    public static async Task<bool> BackfillAsync(AppDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        if (await db.SeedRuns.AnyAsync(r => r.Name == BackfillRun, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var ids = await db.Recipes.IgnoreQueryFilters().Select(r => r.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        await UpdateManyAsync(db, ids, cancellationToken).ConfigureAwait(false);
        db.SeedRuns.Add(new SeedRun { Name = BackfillRun, AppliedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
