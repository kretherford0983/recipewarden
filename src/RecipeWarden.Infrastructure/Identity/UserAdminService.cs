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

/// <summary>FR-INST-04, SEC-05, SEC-12: account creation outside the web UI (CLI and installer).</summary>
public sealed class UserAdminService(UserManager<AppUser> users)
{
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
