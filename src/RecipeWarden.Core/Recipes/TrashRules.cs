using RecipeWarden.Core.Security;

namespace RecipeWarden.Core.Recipes;

/// <summary>
/// BR-05, FR-REC-04: deleting sends a recipe to trash; it can be restored for 30 days and is then purged
/// automatically. Only an Admin can purge immediately.
/// </summary>
public static class TrashRules
{
    public static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(30);

    public static bool IsPurgeDue(DateTimeOffset deletedAt, DateTimeOffset now) => now - deletedAt >= RetentionPeriod;

    public static bool CanRestore(DateTimeOffset deletedAt, DateTimeOffset now) => !IsPurgeDue(deletedAt, now);

    /// <summary>Recipes deleted at or before this moment are due for purge.</summary>
    public static DateTimeOffset PurgeCutoff(DateTimeOffset now) => now - RetentionPeriod;

    /// <summary>When a recipe deleted at <paramref name="deletedAt"/> will be purged (shown in the trash list).</summary>
    public static DateTimeOffset PurgesOn(DateTimeOffset deletedAt) => deletedAt + RetentionPeriod;

    public static bool CanPurgeImmediately(IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        return roles.Any(r => AppPolicies.AdminRoles.Contains(r, StringComparer.Ordinal));
    }
}
