using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Kitchen;

/// <summary>
/// M3 startup seeds, each run once (<see cref="SeedRun"/>): the standard kitchen set (ADR 0021) and the starting
/// food forms (ADR 0018). A form an editor changed later is never reset.
/// </summary>
public sealed class KitchenSeeder(AppDbContext db, KitchenService kitchen, TimeProvider clock)
{
    public const string FoodFormSeed = "food-form-v1";

    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        await kitchen.SeedAsync(cancellationToken).ConfigureAwait(false);
        if (await db.SeedRuns.AnyAsync(r => r.Name == FoodFormSeed, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        foreach (var food in await db.Foods.Where(f => f.Form == FoodForm.Other).ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            food.Form = FoodRules.DefaultForm(food.Name, food.Category);
        }

        db.SeedRuns.Add(new SeedRun { Name = FoodFormSeed, AppliedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
