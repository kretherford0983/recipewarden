using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Web.Pages.Account;

/// <summary>
/// Section 7.2 /account/password, SEC-01, SEC-03: change your own password (current one required). The new one must
/// meet the 12-character rule and not be on the common-password list; other sessions end within 5 minutes.
/// </summary>
public sealed class PasswordModel(UserManager<AppUser> users, SignInManager<AppUser> signIn) : PageModel
{
    public IReadOnlyList<string> Errors { get; private set; } = [];

    public string? Message { get; private set; }

    public void OnGet(string? done) => Message = done == "changed" ? "Password changed. Your other sessions end within 5 minutes." : null;

    public async Task<IActionResult> OnPostAsync(string? current, string? password, string? confirm)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (!string.Equals(password, confirm, StringComparison.Ordinal))
        {
            return Fail("The two new passwords do not match.");
        }

        var result = await users.ChangePasswordAsync(user, current ?? string.Empty, password ?? string.Empty);
        if (!result.Succeeded)
        {
            return Fail(result.Errors.Select(e => e.Code == "PasswordMismatch" ? "The current password is not right." : e.Description).ToArray());
        }

        await signIn.RefreshSignInAsync(user);
        return Redirect("/account/password?done=changed");
    }

    private PageResult Fail(params string[] errors)
    {
        Errors = errors;
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return Page();
    }
}
