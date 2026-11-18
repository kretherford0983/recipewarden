using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Security;

namespace RecipeWarden.Infrastructure.Data;

/// <summary>Applies migrations, switches the database to WAL (8.1, NFR-07) and seeds the roles.</summary>
public sealed class DatabaseInitializer(AppDbContext db, RoleManager<IdentityRole> roles)
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
    }

    internal static string Describe(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(e => e.Description));
}
