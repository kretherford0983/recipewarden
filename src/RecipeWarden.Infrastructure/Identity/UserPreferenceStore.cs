using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Preferences;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Identity;

/// <summary>FR-UI-01: theme saved to a signed-in user's profile.</summary>
public sealed class UserPreferenceStore(AppDbContext db, TimeProvider clock)
{
    public async Task<Theme?> GetThemeAsync(string userId, CancellationToken cancellationToken = default)
    {
        var pref = await db.UserPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken).ConfigureAwait(false);
        return pref?.Theme;
    }

    public async Task SetThemeAsync(string userId, Theme theme, CancellationToken cancellationToken = default)
    {
        var pref = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (pref is null)
        {
            pref = new UserPreference { UserId = userId };
            db.UserPreferences.Add(pref);
        }

        pref.Theme = theme;
        pref.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
