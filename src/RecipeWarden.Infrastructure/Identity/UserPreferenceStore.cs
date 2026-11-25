using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Preferences;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Identity;

/// <summary>FR-UI-01, FR-UI-02: theme, unit system and default scale saved to a signed-in user's profile.</summary>
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

    /// <summary>FR-UI-02: the saved unit system and default scale; nulls when never set.</summary>
    public async Task<(DisplayUnits? Units, decimal? DefaultScale)> GetDisplayAsync(string userId, CancellationToken cancellationToken = default)
    {
        var pref = await db.UserPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken).ConfigureAwait(false);
        return (pref?.Units, pref?.DefaultScale);
    }

    /// <summary>FR-UI-02: a default scale outside 0.1x–100x is not saved (1x is stored as no default).</summary>
    public async Task SetDisplayAsync(string userId, DisplayUnits units, decimal? defaultScale, CancellationToken cancellationToken = default)
    {
        var pref = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (pref is null)
        {
            pref = new UserPreference { UserId = userId };
            db.UserPreferences.Add(pref);
        }

        pref.Units = units;
        pref.DefaultScale = defaultScale is { } s && s != 1m && ScaleRules.InRange(s) ? s : null;
        pref.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
