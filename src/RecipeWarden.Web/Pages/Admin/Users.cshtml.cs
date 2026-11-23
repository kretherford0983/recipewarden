using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Identity;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Admin;

/// <summary>
/// FR-AUTH-03, SEC-05, SEC-12, SEC-13, AC-72: Admins create accounts and change roles, lock, unlock, reset passwords
/// and delete. The last Admin cannot be demoted, locked or deleted, and nobody acts on their own account
/// (UserAdminRules in Core). Every change is audited by the interceptor (User.*, UserRole.*).
/// </summary>
public sealed class UsersModel(UserAdminService accounts) : PageModel
{
    /// <summary>SEC-13: only these fields bind.</summary>
    public sealed class NewUserInput
    {
        [Required, StringLength(254)]
        public string Email { get; set; } = string.Empty;

        [Required, StringLength(1024)]
        public string Password { get; set; } = string.Empty;

        [Required]
        public string Role { get; set; } = AppRoles.Editor;
    }

    [BindProperty]
    public NewUserInput NewUser { get; set; } = new();

    public IReadOnlyList<UserSummary> Users { get; private set; } = [];

    public string? Message { get; private set; }

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public string CurrentUserId => Permissions.UserId(User);

    public async Task OnGetAsync(string? done)
    {
        Message = done switch
        {
            "created" => "Account created. Give the person their email and password; they can change the password after signing in.",
            "role" => "Role changed. The person's other sessions end within 5 minutes.",
            "locked" => "Account locked.",
            "unlocked" => "Account unlocked.",
            "password" => "Password set. The person's other sessions end within 5 minutes.",
            "deleted" => "Account deleted.",
            "twofactor" => "Two-factor sign-in turned off. The person can sign in with their password and set it up again.",
            _ => null,
        };
        Users = await accounts.ListAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var result = await accounts.CreateUserAsync(NewUser.Email.Trim(), NewUser.Password, NewUser.Role);
        return await DoneAsync(result, "created");
    }

    public async Task<IActionResult> OnPostRoleAsync(string id, string role) =>
        await DoneAsync(await accounts.ChangeRoleAsync(CurrentUserId, id, role), "role");

    public async Task<IActionResult> OnPostLockAsync(string id) =>
        await DoneAsync(await accounts.SetLockedAsync(CurrentUserId, id, locked: true), "locked");

    public async Task<IActionResult> OnPostUnlockAsync(string id) =>
        await DoneAsync(await accounts.SetLockedAsync(CurrentUserId, id, locked: false), "unlocked");

    public async Task<IActionResult> OnPostPasswordAsync(string id, string password) =>
        await DoneAsync(await accounts.ResetPasswordAsync(id, password ?? string.Empty), "password");

    /// <summary>SEC-04, ADR 0043: for someone who lost their authenticator and recovery codes.</summary>
    public async Task<IActionResult> OnPostTwoFactorOffAsync(string id) =>
        await DoneAsync(await accounts.DisableTwoFactorAsync(CurrentUserId, id), "twofactor");

    public async Task<IActionResult> OnPostDeleteAsync(string id) =>
        await DoneAsync(await accounts.DeleteAsync(CurrentUserId, id), "deleted");

    private async Task<IActionResult> DoneAsync(AccountResult result, string done)
    {
        if (result.Succeeded)
        {
            return Redirect("/admin/users?done=" + done);
        }

        Errors = result.Errors;
        Users = await accounts.ListAsync();
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return Page();
    }
}
