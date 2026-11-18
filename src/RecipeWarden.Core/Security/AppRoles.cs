namespace RecipeWarden.Core.Security;

/// <summary>SEC-11, D-04: the two roles. Code checks policies, never these strings directly.</summary>
public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Editor = "Editor";

    public static IReadOnlyList<string> All { get; } = [Admin, Editor];
}

/// <summary>SEC-11: authorization policy names.</summary>
public static class AppPolicies
{
    /// <summary>FR-AUTH-02: create, edit, delete, import; manage tags, collections, inventory.</summary>
    public const string CanEditRecipes = "CanEditRecipes";

    /// <summary>FR-AUTH-03: settings, users, backups, restore.</summary>
    public const string CanAdminister = "CanAdminister";

    /// <summary>Roles that satisfy <see cref="CanEditRecipes"/>.</summary>
    public static IReadOnlyList<string> EditorRoles { get; } = [AppRoles.Editor, AppRoles.Admin];

    /// <summary>Roles that satisfy <see cref="CanAdminister"/>.</summary>
    public static IReadOnlyList<string> AdminRoles { get; } = [AppRoles.Admin];
}

/// <summary>SEC-01, SEC-02, SEC-03: account and session rules.</summary>
public static class AccountRules
{
    public const int MinimumPasswordLength = 12;
    public const int MaxFailedAccessAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan SessionSlidingExpiry = TimeSpan.FromHours(8);
    public static readonly TimeSpan SecurityStampValidationInterval = TimeSpan.FromMinutes(5);

    /// <summary>SEC-02: the only message shown for any failed sign-in.</summary>
    public const string InvalidSignInMessage = "Invalid sign-in attempt.";
}
