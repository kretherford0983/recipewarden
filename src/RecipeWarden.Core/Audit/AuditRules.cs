namespace RecipeWarden.Core.Audit;

/// <summary>What happened to an entity.</summary>
public enum AuditChange
{
    Create,
    Update,
    Delete,
}

/// <summary>
/// FR-ADM-01, AC-74, SEC-72: naming and filtering of audit entries. Every write is recorded with who, what, when and
/// from which IP; details hold the names of changed properties only, never values, so passwords, tokens and keys can
/// never reach the log (ADR 0041).
/// </summary>
public static class AuditRules
{
    public const int MaxActionLength = 64;
    public const int MaxEntityIdLength = 128;
    public const int MaxDetailsLength = 4000;
    public const int PageSize = 50;

    /// <summary>The actor recorded for writes no person made: the scheduler, the health monitor, migrations.</summary>
    public const string SystemActor = "system";

    /// <summary>
    /// Bookkeeping rows written by the app itself, not by people (each kind is audited where it matters: a backup run
    /// writes "Backup.Run"). The audit log itself is append-only and never audits its own rows.
    /// </summary>
    public static IReadOnlySet<string> IgnoredEntities { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "AuditEntry", "BackupRecord", "StorageSnapshot", "HealthCheckResult", "SeedRun", "RecipeAllergen",
        "InventorySnapshot", "IdentityUserToken", "IdentityUserLogin", "AiCallLog",
    };

    /// <summary>
    /// Identity columns that change on every sign-in or failed attempt. An update touching only these is the sign-in
    /// itself, which is audited explicitly (Account.SignIn / Account.SignInFailed) instead.
    /// </summary>
    public static IReadOnlySet<string> SignInBookkeeping { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "AccessFailedCount", "LockoutEnd", "ConcurrencyStamp", "SecurityStamp",
    };

    /// <summary>"Recipe.Create", "User.Update", "UserRole.Delete".</summary>
    public static string ActionName(string entity, AuditChange change)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var name = entity switch
        {
            "AppUser" => "User",
            "IdentityUserRole" => "UserRole",
            "IdentityRole" => "Role",
            "RecipeCollection" => "Collection",
            _ => entity,
        };
        var action = name + "." + change;
        return action.Length <= MaxActionLength ? action : action[..MaxActionLength];
    }

    /// <summary>
    /// Whether an entity change is worth an audit row: ignored bookkeeping entities never are; an update that only
    /// touched sign-in counters is not.
    /// </summary>
    public static bool ShouldAudit(string entity, AuditChange change, IReadOnlyCollection<string> changedProperties)
    {
        ArgumentNullException.ThrowIfNull(changedProperties);
        if (IgnoredEntities.Contains(entity))
        {
            return false;
        }

        if (change != AuditChange.Update)
        {
            return true;
        }

        if (changedProperties.Count == 0)
        {
            return false;
        }

        return !(entity == "AppUser" && changedProperties.All(SignInBookkeeping.Contains));
    }

    public static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..max];
}
