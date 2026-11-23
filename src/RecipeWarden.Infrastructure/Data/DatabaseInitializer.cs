using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Kitchen;
using RecipeWarden.Infrastructure.Measurement;
using RecipeWarden.Infrastructure.Nutrition;
using RecipeWarden.Infrastructure.Shopping;

namespace RecipeWarden.Infrastructure.Data;

/// <summary>
/// Applies migrations, switches the database to WAL (8.1, NFR-07), seeds the roles and the measurement catalogue
/// (once, ADR 0013), the kitchen's standard set and food forms (once, ADR 0021, ADR 0018), and links ingredients that
/// have no food yet.
/// </summary>
public sealed class DatabaseInitializer(AppDbContext db, RoleManager<IdentityRole> roles, MeasurementSeeder seeder,
    FoodCatalogService foods, KitchenSeeder kitchen, ShoppingSeeder shopping,
    NutrientDatasetImporter nutrients, TimeProvider clock)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        // journal_mode is persistent in the database file, so setting it once is enough.
        await db.Database.ExecuteSqlAsync($"PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);

        foreach (var role in AppRoles.All)
        {
            if (!await roles.RoleExistsAsync(role).ConfigureAwait(false))
            {
                var result = await roles.CreateAsync(new IdentityRole(role)).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException($"Could not create role {role}: {Describe(result)}");
                }
            }
        }

        await seeder.ApplyAsync(cancellationToken).ConfigureAwait(false);
        await kitchen.ApplyAsync(cancellationToken).ConfigureAwait(false); // ADR 0021, ADR 0018 (after the foods exist)
        await shopping.ApplyAsync(cancellationToken).ConfigureAwait(false); // ADR 0030
        await nutrients.ApplySeedAsync(cancellationToken).ConfigureAwait(false); // ADR 0034
        await foods.LinkUnmatchedAsync(cancellationToken).ConfigureAwait(false);
        await RecipeAllergenIndex.BackfillAsync(db, clock, cancellationToken).ConfigureAwait(false); // ADR 0036
    }

    internal static string Describe(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(e => e.Description));
}
