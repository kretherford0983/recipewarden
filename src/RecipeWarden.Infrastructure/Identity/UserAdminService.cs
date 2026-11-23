using Microsoft.AspNetCore.Identity;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Identity;

/// <summary>Outcome of an account operation, with messages safe to show an operator.</summary>
public sealed record AccountResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static AccountResult Ok { get; } = new(true, []);

    public static AccountResult Fail(params string[] errors) => new(false, errors);
}

/// <summary>A row on /admin/users.</summary>
public sealed record UserSummary(string Id, string Email, string Role, bool LockedOut, bool TwoFactor, DateTimeOffset? LockoutEnd);

/// <summary>
/// FR-INST-04, SEC-05, SEC-12, AC-72: account creation (CLI, installer, /setup) and user management on /admin/users.
/// Only Admins reach these methods (policy); the last-Admin and own-account rules are in Core (UserAdminRules).
/// Role changes and locks update the security stamp, so the user's other sessions end within 5 minutes (SEC-03).
/// </summary>
public sealed class UserAdminService(UserManager<AppUser> users)
{
    public static readonly DateTimeOffset LockedForever = DateTimeOffset.MaxValue;

    public async Task<IReadOnlyList<UserSummary>> ListAsync()
    {
        var admins = (await users.GetUsersInRoleAsync(AppRoles.Admin).ConfigureAwait(false)).Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        var editors = (await users.GetUsersInRoleAsync(AppRoles.Editor).ConfigureAwait(false)).Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        return users.Users.OrderBy(u => u.Email).ToList()
            .Select(u => new UserSummary(u.Id, u.Email ?? u.UserName ?? u.Id,
                admins.Contains(u.Id) ? AppRoles.Admin : editors.Contains(u.Id) ? AppRoles.Editor : UserAdminRules.NoRole,
                u.LockoutEnd is { } end && end > DateTimeOffset.UtcNow, u.TwoFactorEnabled, u.LockoutEnd))
            .ToList();
    }

    /// <summary>An Admin creates an account with a role and a first password (no self-registration, SEC-05).</summary>
    public async Task<AccountResult> CreateUserAsync(string email, string password, string role)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (!UserAdminRules.AssignableRoles.Contains(role))
        {
            return AccountResult.Fail("Choose Admin, Editor or None.");
        }

        var check = await CheckNewAdminEmailAsync(email).ConfigureAwait(false);
        if (!check.Succeeded)
        {
            return check;
        }

        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
        var created = await users.CreateAsync(user, password).ConfigureAwait(false);
        if (!created.Succeeded)
        {
            return AccountResult.Fail(created.Errors.Select(e => e.Description).ToArray());
        }

        if (role != UserAdminRules.NoRole)
        {
            var added = await users.AddToRoleAsync(user, role).ConfigureAwait(false);
            if (!added.Succeeded)
            {
                await users.DeleteAsync(user).ConfigureAwait(false);
                return AccountResult.Fail(added.Errors.Select(e => e.Description).ToArray());
            }
        }

        return AccountResult.Ok;
    }

    public async Task<AccountResult> ChangeRoleAsync(string actorId, string targetId, string newRole)
    {
        var (target, isAdmin, admins) = await LoadAsync(targetId).ConfigureAwait(false);
        if (target is null)
        {
            return AccountResult.Fail("That account no longer exists.");
        }

        if (UserAdminRules.Check(UserAction.ChangeRole, actorId, targetId, isAdmin, admins, newRole) is { } refused)
        {
            return AccountResult.Fail(refused);
        }

        var current = await users.GetRolesAsync(target).ConfigureAwait(false);
        var remove = current.Where(r => r != newRole).ToList();
        if (remove.Count > 0)
        {
            await users.RemoveFromRolesAsync(target, remove).ConfigureAwait(false);
        }

        if (newRole != UserAdminRules.NoRole && !current.Contains(newRole))
        {
            await users.AddToRoleAsync(target, newRole).ConfigureAwait(false);
        }

        await users.UpdateSecurityStampAsync(target).ConfigureAwait(false);
        return AccountResult.Ok;
    }

    public async Task<AccountResult> SetLockedAsync(string actorId, string targetId, bool locked)
    {
        var (target, isAdmin, admins) = await LoadAsync(targetId).ConfigureAwait(false);
        if (target is null)
        {
            return AccountResult.Fail("That account no longer exists.");
        }

        if (locked && UserAdminRules.Check(UserAction.Lock, actorId, targetId, isAdmin, admins) is { } refused)
        {
            return AccountResult.Fail(refused);
        }

        await users.SetLockoutEnabledAsync(target, true).ConfigureAwait(false);
        await users.SetLockoutEndDateAsync(target, locked ? LockedForever : null).ConfigureAwait(false);
        if (!locked)
        {
            await users.ResetAccessFailedCountAsync(target).ConfigureAwait(false);
        }

        await users.UpdateSecurityStampAsync(target).ConfigureAwait(false);
        return AccountResult.Ok;
    }

    public async Task<AccountResult> DeleteAsync(string actorId, string targetId)
    {
        var (target, isAdmin, admins) = await LoadAsync(targetId).ConfigureAwait(false);
        if (target is null)
        {
            return AccountResult.Fail("That account no longer exists.");
        }

        if (UserAdminRules.Check(UserAction.Delete, actorId, targetId, isAdmin, admins) is { } refused)
        {
            return AccountResult.Fail(refused);
        }

        var result = await users.DeleteAsync(target).ConfigureAwait(false);
        return result.Succeeded ? AccountResult.Ok : AccountResult.Fail(result.Errors.Select(e => e.Description).ToArray());
    }

    /// <summary>
    /// SEC-04, ADR 0043: an Admin turns two-factor off for someone who lost their authenticator and recovery codes. Not
    /// for your own account (use /account/2fa, which needs a current code). The key and codes are cleared; sessions end.
    /// </summary>
    public async Task<AccountResult> DisableTwoFactorAsync(string actorId, string targetId)
    {
        if (actorId == targetId)
        {
            return AccountResult.Fail("Turn off your own two-factor sign-in on your account page.");
        }

        var target = await users.FindByIdAsync(targetId).ConfigureAwait(false);
        if (target is null)
        {
            return AccountResult.Fail("That account no longer exists.");
        }

        await users.SetTwoFactorEnabledAsync(target, false).ConfigureAwait(false);
        await users.ResetAuthenticatorKeyAsync(target).ConfigureAwait(false); // a new key nobody has yet; also renews the stamp
        await users.GenerateNewTwoFactorRecoveryCodesAsync(target, 0).ConfigureAwait(false); // old recovery codes stop working
        await users.UpdateSecurityStampAsync(target).ConfigureAwait(false);
        return AccountResult.Ok;
    }

    /// <summary>An Admin sets a new password for someone who forgot theirs; their sessions end.</summary>
    public async Task<AccountResult> ResetPasswordAsync(string targetId, string newPassword)
    {
        var target = await users.FindByIdAsync(targetId).ConfigureAwait(false);
        if (target is null)
        {
            return AccountResult.Fail("That account no longer exists.");
        }

        var token = await users.GeneratePasswordResetTokenAsync(target).ConfigureAwait(false);
        var result = await users.ResetPasswordAsync(target, token, newPassword).ConfigureAwait(false);
        return result.Succeeded ? AccountResult.Ok : AccountResult.Fail(result.Errors.Select(e => e.Description).ToArray());
    }

    private async Task<(AppUser? Target, bool IsAdmin, int AdminCount)> LoadAsync(string targetId)
    {
        var target = await users.FindByIdAsync(targetId).ConfigureAwait(false);
        var admins = await users.GetUsersInRoleAsync(AppRoles.Admin).ConfigureAwait(false);
        return (target, target is not null && admins.Any(a => a.Id == target.Id), admins.Count);
    }

    /// <summary>AC-100: the email checks that need no password, so callers can run them before asking for one.</summary>
    public async Task<AccountResult> CheckNewAdminEmailAsync(string email)
    {
        if (AccountRules.ValidateEmail(email) is { } invalid)
        {
            return AccountResult.Fail(invalid);
        }

        if (await users.FindByEmailAsync(email).ConfigureAwait(false) is not null)
        {
            return AccountResult.Fail($"An account for {email} already exists.");
        }

        return AccountResult.Ok;
    }

    public async Task<AccountResult> CreateAdminAsync(string email, string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var check = await CheckNewAdminEmailAsync(email).ConfigureAwait(false);
        if (!check.Succeeded)
        {
            return check;
        }

        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
        var created = await users.CreateAsync(user, password).ConfigureAwait(false);
        if (!created.Succeeded)
        {
            return AccountResult.Fail(created.Errors.Select(e => e.Description).ToArray());
        }

        var role = await users.AddToRoleAsync(user, AppRoles.Admin).ConfigureAwait(false);
        if (!role.Succeeded)
        {
            await users.DeleteAsync(user).ConfigureAwait(false);
            return AccountResult.Fail(role.Errors.Select(e => e.Description).ToArray());
        }

        return AccountResult.Ok;
    }
}
