namespace RecipeWarden.Core.Security;

/// <summary>What an Admin can do to an account on /admin/users.</summary>
public enum UserAction
{
    ChangeRole,
    Lock,
    Delete,
}

/// <summary>
/// SEC-12, AC-72: only Admins manage users (checked by the policy); the last Admin cannot be demoted, locked or
/// deleted; nobody changes their own role, locks or deletes themselves.
/// </summary>
public static class UserAdminRules
{
    public const string NoRole = "None";

    /// <summary>The roles an Admin can assign: Admin, Editor, or none (a reader who can sign in).</summary>
    public static IReadOnlyList<string> AssignableRoles { get; } = [AppRoles.Admin, AppRoles.Editor, NoRole];

    /// <summary>Null when allowed, otherwise the reason shown to the Admin.</summary>
    /// <param name="newRole">For <see cref="UserAction.ChangeRole"/>: Admin, Editor or None.</param>
    public static string? Check(UserAction action, string actorId, string targetId, bool targetIsAdmin, int adminCount, string? newRole = null)
    {
        if (string.Equals(actorId, targetId, StringComparison.Ordinal))
        {
            return action switch
            {
                UserAction.ChangeRole => "You cannot change your own role. Ask another Admin.",
                UserAction.Lock => "You cannot lock your own account.",
                _ => "You cannot delete your own account.",
            };
        }

        if (action == UserAction.ChangeRole && (newRole is null || !AssignableRoles.Contains(newRole)))
        {
            return "Choose Admin, Editor or None.";
        }

        var removesAdmin = action != UserAction.ChangeRole || !string.Equals(newRole, AppRoles.Admin, StringComparison.Ordinal);
        if (targetIsAdmin && removesAdmin && adminCount <= 1)
        {
            return "This is the last Admin account. Make another account an Admin first.";
        }

        return null;
    }
}
